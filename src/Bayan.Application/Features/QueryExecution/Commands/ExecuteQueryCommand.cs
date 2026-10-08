using System.Diagnostics;
using System.Text.Json;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Application.Common.Models;
using Bayan.Domain.Constants;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.QueryExecution.Commands;

/// <summary>
/// Executes a dynamic query with user-provided parameters.
/// Enforces strict parameterization — no string concatenation.
/// Uses the database user configured on the query (managed via the admin query page).
/// </summary>
public class ExecuteQueryCommand : IRequest<QueryExecutionResult>
{
    public Guid QueryId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();

    /// <summary>
    /// When false (default), write queries (INSERT/UPDATE/DELETE) are executed in
    /// preview mode: the change is run inside a transaction and rolled back, and
    /// only the affected row count is returned so the user can confirm. The client
    /// must re-submit with Confirmed=true to actually commit the change.
    /// </summary>
    public bool Confirmed { get; set; }

    /// <summary>
    /// When true, the query runs with no row cap so the full result set is returned (used by
    /// the Excel export). Only valid for queries that return rows — write queries are rejected.
    /// </summary>
    public bool Unlimited { get; set; }

    /// <summary>
    /// When true, a read query runs with no row cap so the complete result set can be cached
    /// server-side and served to the grid page-by-page (and reused for export) from a single
    /// execution. Unlike <see cref="Unlimited"/>, write queries are not rejected — they fall
    /// through to the normal preview/confirm path unchanged.
    /// </summary>
    public bool CacheFullResult { get; set; }
}

public class ExecuteQueryCommandHandler : IRequestHandler<ExecuteQueryCommand, QueryExecutionResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IQueryExecutor _queryExecutor;
    private readonly ICurrentUserService _currentUser;
    private readonly IEncryptionService _encryption;
    private readonly IDatabaseConnectionFactory _connectionFactory;

    public ExecuteQueryCommandHandler(
        IUnitOfWork unitOfWork,
        IQueryExecutor queryExecutor,
        ICurrentUserService currentUser,
        IEncryptionService encryption,
        IDatabaseConnectionFactory connectionFactory)
    {
        _unitOfWork = unitOfWork;
        _queryExecutor = queryExecutor;
        _currentUser = currentUser;
        _encryption = encryption;
        _connectionFactory = connectionFactory;
    }

    public async Task<QueryExecutionResult> Handle(
        ExecuteQueryCommand request,
        CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.QueryId, cancellationToken);
        if (query is null)
            throw new NotFoundException(nameof(DynamicQuery), request.QueryId);

        if (!query.IsEnabled)
            throw new DomainException("This query is currently disabled.");

        // Verify user has access via roles, user groups, direct assignment or the query group
        await QueryAccess.EnsureCanRunAsync(query, _unitOfWork, _currentUser, cancellationToken);

        // Use the database user configured on the query
        var effectiveDbUserId = query.DatabaseUserId;
        string? connectionString = null;
        DatabaseUser? resolvedDbUser = null;

        if (effectiveDbUserId.HasValue)
        {
            resolvedDbUser = await ResolveAndValidateDbUserAsync(effectiveDbUserId.Value, cancellationToken);
            var password = _encryption.Decrypt(resolvedDbUser.EncryptedPassword);
            connectionString = _connectionFactory.BuildConnectionString(resolvedDbUser, password);
        }

        // Build typed parameters from metadata
        var queryParams = await _unitOfWork.QueryParameters.FindAsync(
            p => p.DynamicQueryId == request.QueryId, cancellationToken);

        var typedParameters = QueryParameterBinder.Bind(queryParams, request.Parameters);

        // Export runs the full result set with no row cap; it only makes sense for queries
        // that return rows, so reject write queries up front.
        if (request.Unlimited && query.QueryType.IsWrite())
            throw new DomainException("Export is only available for queries that return rows.");

        // Preview mode: for unconfirmed write queries (INSERT/UPDATE/DELETE), run inside
        // a transaction, capture the affected row count, then roll back. The client shows
        // the count to the user and re-submits with Confirmed=true to actually commit.
        if (!request.Confirmed && query.QueryType.IsWrite())
        {
            var previewSw = Stopwatch.StartNew();
            try
            {
                var previewResult = await _queryExecutor.ExecutePreviewAsync(
                    query.SqlQuery,
                    typedParameters,
                    query.TimeoutSeconds,
                    connectionString,
                    resolvedDbUser?.ServerType,
                    cancellationToken);
                previewResult.Parameters = typedParameters;

                // For UPDATE/DELETE, also fetch the affected rows so the user can review them.
                var affectedRowsPreview = await AffectedRowsPreview.FetchAsync(
                    _queryExecutor, query.SqlQuery, typedParameters, query.TimeoutSeconds, connectionString, resolvedDbUser?.ServerType, cancellationToken);
                if (affectedRowsPreview is not null)
                {
                    previewResult.PreviewColumns = affectedRowsPreview.Columns;
                    previewResult.PreviewRows = affectedRowsPreview.Rows;
                }

                return previewResult;
            }
            catch (Exception ex)
            {
                previewSw.Stop();
                var previewLog = new QueryExecutionLog
                {
                    Id = Guid.NewGuid(),
                    DynamicQueryId = request.QueryId,
                    UserId = _currentUser.UserId,
                    ParametersJson = JsonSerializer.Serialize(request.Parameters),
                    ExecutedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    ExecutionDurationMs = previewSw.ElapsedMilliseconds,
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
                await _unitOfWork.QueryExecutionLogs.AddAsync(previewLog, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                throw;
            }
        }

        // For UPDATE/DELETE, capture the current rows that match the WHERE clause so the
        // audit log preserves the pre-change state of every affected row. Admins can turn this
        // off per query (SaveOldValues): on statements that affect large row counts the extra
        // SELECT and the stored copy of every affected row are the expensive part of the run.
        string? oldValuesJson = null;
        if (query.SaveOldValues && query.QueryType is QueryType.Update or QueryType.Delete)
        {
            var oldRows = await AffectedRowsPreview.FetchAsync(
                _queryExecutor, query.SqlQuery, typedParameters, query.TimeoutSeconds, connectionString, resolvedDbUser?.ServerType, cancellationToken);
            if (oldRows is not null && oldRows.Rows.Count > 0)
            {
                var serializable = oldRows.Rows.Select(r =>
                    r.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "NULL"));
                oldValuesJson = JsonSerializer.Serialize(serializable);
            }
        }

        var log = new QueryExecutionLog
        {
            Id = Guid.NewGuid(),
            DynamicQueryId = request.QueryId,
            UserId = _currentUser.UserId,
            ParametersJson = JsonSerializer.Serialize(request.Parameters),
            OldValuesJson = oldValuesJson,
            ExecutedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var sw = Stopwatch.StartNew();
        try
        {
            // No row cap when exporting, or when caching a read's full result set for paged
            // display + export from a single run. For write queries CacheFullResult is a no-op
            // here (they never reach this point unconfirmed, and a confirmed write returns a
            // count, not rows).
            var noRowCap = request.Unlimited
                || (request.CacheFullResult && !query.QueryType.IsWrite());

            QueryExecutionResult result;
            if (noRowCap)
            {
                // No row cap so the cached result / export contains the complete result set.
                result = await _queryExecutor.ExecuteAsync(
                    query.SqlQuery, typedParameters, query.TimeoutSeconds, connectionString, resolvedDbUser?.ServerType, int.MaxValue, cancellationToken);
            }
            else if (connectionString != null && resolvedDbUser != null)
            {
                result = await _queryExecutor.ExecuteAsync(
                    query.SqlQuery, typedParameters, query.TimeoutSeconds, connectionString, resolvedDbUser.ServerType, cancellationToken);
            }
            else
            {
                result = await _queryExecutor.ExecuteAsync(
                    query.SqlQuery, typedParameters, query.TimeoutSeconds, cancellationToken);
            }

            sw.Stop();
            result.Parameters = typedParameters;
            log.ExecutionDurationMs = sw.ElapsedMilliseconds;
            log.RowsReturned = result.TotalRows > 0 ? result.TotalRows : result.AffectedRows;
            log.IsSuccess = true;

            await _unitOfWork.QueryExecutionLogs.AddAsync(log, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            log.ExecutionDurationMs = sw.ElapsedMilliseconds;
            log.IsSuccess = false;
            log.ErrorMessage = ex.Message;

            await _unitOfWork.QueryExecutionLogs.AddAsync(log, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            throw;
        }
    }

    /// <summary>
    /// Resolves and validates a database user, checking the current user has access.
    /// Returns the DatabaseUser entity for connection string building.
    /// </summary>
    private Task<DatabaseUser> ResolveAndValidateDbUserAsync(Guid databaseUserId, CancellationToken cancellationToken) =>
        QueryAccess.EnsureCanUseDatabaseUserAsync(databaseUserId, _unitOfWork, _currentUser, cancellationToken);
}
