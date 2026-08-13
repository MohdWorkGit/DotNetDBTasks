using System.Text.Json;
using System.Text.Json.Nodes;
using Bayan.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Bayan.Application.Common.Behaviors;

/// <summary>
/// Writes a <c>SystemAuditLog</c> row for every command that passes through MediatR.
///
/// <para>
/// A pipeline behavior rather than a call in each of the ~30 handlers: the audit trail is only
/// worth trusting if it is complete, and hand-instrumenting means a handler added next month is
/// silently missing. Anything that must not be recorded is named in
/// <see cref="AuditActions.IsExcluded"/>, so the omissions are a short explicit list instead of
/// an unbounded set of oversights.
/// </para>
///
/// <para>
/// Failures are recorded too. "Access Manager tried to reset the admin's password and was
/// refused" is exactly the kind of entry an auditor wants, and it only exists if the behavior
/// logs the exception path as well as the happy one.
/// </para>
/// </summary>
public class AuditLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Property names whose values never reach the audit table. Matched as a substring against
    /// the lower-cased property name, so <c>Password</c>, <c>NewPassword</c> and
    /// <c>EncryptedPassword</c> are all caught by one entry.
    /// </summary>
    private static readonly string[] SecretFragments =
    {
        "password", "secret", "token", "connectionstring", "encrypted", "apikey", "credential"
    };

    /// <summary>
    /// Properties that carry a file's bytes or a full SQL body. Dropped for size, not secrecy —
    /// a 5 MB template would otherwise be base64'd into every audit row.
    /// </summary>
    private static readonly string[] BulkFragments = { "content", "filebytes", "bytes" };

    /// <summary>
    /// Beyond this the payload is replaced with a marker rather than stored. Matches the
    /// column's NVARCHAR2(2000) — exceed it and Oracle rejects the insert with ORA-12899,
    /// which would lose the audit row entirely.
    /// </summary>
    private const int MaxDetailsLength = 2000;

    private readonly IAuditLogger _audit;
    private readonly ILogger<AuditLoggingBehavior<TRequest, TResponse>> _logger;

    public AuditLoggingBehavior(
        IAuditLogger audit,
        ILogger<AuditLoggingBehavior<TRequest, TResponse>> logger)
    {
        _audit = audit;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var typeName = typeof(TRequest).Name;

        // Queries vastly outnumber commands; only commands change anything.
        if (!typeName.EndsWith("Command", StringComparison.Ordinal) || AuditActions.IsExcluded(typeName))
            return await next();

        var (action, category) = AuditActions.Resolve(typeName);

        try
        {
            var response = await next();
            await RecordAsync(request, action, category, success: true, error: null, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            // Record the refusal, then let the exception continue to the middleware unchanged —
            // auditing must not swallow or alter the caller's error.
            await RecordAsync(request, action, category, success: false, error: ex.Message, cancellationToken);
            throw;
        }
    }

    private async Task RecordAsync(
        TRequest request, string action, string category,
        bool success, string? error, CancellationToken cancellationToken)
    {
        try
        {
            var details = Sanitize(request);
            var json = details?.ToJsonString();

            // Replaced wholesale rather than cut: half a JSON document is worse than none,
            // because it looks parseable and is not.
            if (json is not null && json.Length > MaxDetailsLength)
                json = $"{{\"omitted\":\"payload too large ({json.Length} chars)\"}}";

            await _audit.RecordAsync(new AuditEntry
            {
                Action = action,
                Category = category,
                EntityId = ExtractId(details),
                EntityName = ExtractName(details),
                DetailsJson = json,
                IsSuccess = success,
                ErrorMessage = Truncate(error, 2000)
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never let the audit trail break the operation it is describing.
            _logger.LogError(ex, "Failed to write an audit entry for {Action}.", action);
        }
    }

    /// <summary>
    /// Serializes the command, dropping secrets and bulk payloads. Returns null when the
    /// command has nothing worth recording or cannot be serialized.
    /// </summary>
    private static JsonObject? Sanitize(TRequest request)
    {
        JsonNode? node;
        try
        {
            node = JsonSerializer.SerializeToNode(request, request.GetType());
        }
        catch (NotSupportedException)
        {
            // A command holding a stream or similar. The row is still worth writing.
            return null;
        }

        if (node is not JsonObject obj)
            return null;

        foreach (var name in obj.Select(p => p.Key).ToList())
        {
            var lowered = name.ToLowerInvariant();

            if (SecretFragments.Any(f => lowered.Contains(f, StringComparison.Ordinal)))
            {
                // Keep the key so the entry still shows that a password was part of the
                // change, without recording the value itself.
                obj[name] = "***";
            }
            else if (BulkFragments.Any(f => lowered.Contains(f, StringComparison.Ordinal)))
            {
                obj[name] = "[omitted]";
            }
        }

        return obj;
    }

    private static string? ExtractId(JsonObject? details)
    {
        if (details is null) return null;

        // Commands name their target inconsistently; take the first that is present.
        foreach (var candidate in new[] { "id", "userId", "queryId", "groupId", "taskId", "dbUserId" })
        {
            var match = details.FirstOrDefault(p =>
                string.Equals(p.Key, candidate, StringComparison.OrdinalIgnoreCase));
            if (match.Value is not null)
                return Truncate(match.Value.ToString(), 200);
        }

        return null;
    }

    private static string? ExtractName(JsonObject? details)
    {
        if (details is null) return null;

        foreach (var candidate in new[] { "name", "username", "newUsername" })
        {
            var match = details.FirstOrDefault(p =>
                string.Equals(p.Key, candidate, StringComparison.OrdinalIgnoreCase));
            if (match.Value is not null)
                return Truncate(match.Value.ToString(), 400);
        }

        return null;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }
}
