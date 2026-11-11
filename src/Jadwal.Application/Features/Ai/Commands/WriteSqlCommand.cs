using System.Text.RegularExpressions;
using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Common.Security;
using Jadwal.Domain.Entities;
using Jadwal.Domain.Enums;
using Jadwal.Domain.Exceptions;
using FluentValidation;
using MediatR;

namespace Jadwal.Application.Features.Ai.Commands;

/// <summary>
/// Writes a SELECT from a description, against tables the admin picks from what the connection
/// can read. The model is given their structure — names, types, keys — and example values only
/// when System Settings allows it. The SQL is only shown: it must pass <see cref="AiSqlGuard"/>
/// as a read before it is, and the admin tests and saves it like any other query.
/// </summary>
public class WriteSqlCommand : IRequest<AiWrittenSqlDto>
{
    public const int DescriptionMaxLength = 1000;
    public const int MaxObjects = 10;

    public Guid? DatabaseUserId { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>The picked tables and views, by the qualified names the schema listing gave.</summary>
    public List<string> Objects { get; set; } = new();

    /// <summary>
    /// The parameters the query form already has, so the SQL uses them instead of inventing
    /// near-duplicates (@from_date beside @fromDate).
    /// </summary>
    public List<AiExistingParameter> ExistingParameters { get; set; } = new();
}

public class AiExistingParameter
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ParameterType ParameterType { get; set; }
    public bool IsRequired { get; set; }
    public bool AllowMultiple { get; set; }
}

public class WriteSqlValidator : AbstractValidator<WriteSqlCommand>
{
    public WriteSqlValidator(IAppLocalizer messages)
    {
        RuleFor(x => x.Description)
            .NotEmpty().WithMessage(_ => messages[MessageKeys.AiDescriptionRequired])
            .MaximumLength(WriteSqlCommand.DescriptionMaxLength)
            .WithMessage(_ => messages[MessageKeys.AiDescriptionTooLong, WriteSqlCommand.DescriptionMaxLength]);

        RuleFor(x => x.Objects)
            .NotEmpty().WithMessage(_ => messages[MessageKeys.AiTablesRequired])
            .Must(o => o.Distinct(StringComparer.OrdinalIgnoreCase).Count() <= WriteSqlCommand.MaxObjects)
            .WithMessage(_ => messages[MessageKeys.AiTooManyTables, WriteSqlCommand.MaxObjects]);

        RuleFor(x => x.ExistingParameters).Must(p => p.Count <= 50);
    }
}

public partial class WriteSqlCommandHandler : IRequestHandler<WriteSqlCommand, AiWrittenSqlDto>
{
    private static readonly Dictionary<string, ParameterType> ParameterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["string"] = ParameterType.String,
        ["number"] = ParameterType.Number,
        ["date"] = ParameterType.Date,
        ["boolean"] = ParameterType.Boolean,
        ["list"] = ParameterType.Dropdown
    };

    private const int MaxOptions = 50;

    private readonly IAiChatClient _ai;
    private readonly AiConnectionResolver _resolver;
    private readonly ISchemaReader _reader;
    private readonly ISchemaCache _cache;
    private readonly ISystemSettingsService _settings;
    private readonly IAppLocalizer _messages;

    public WriteSqlCommandHandler(
        IAiChatClient ai, AiConnectionResolver resolver, ISchemaReader reader, ISchemaCache cache,
        ISystemSettingsService settings, IAppLocalizer messages)
    {
        _ai = ai;
        _resolver = resolver;
        _reader = reader;
        _cache = cache;
        _settings = settings;
        _messages = messages;
    }

    public async Task<AiWrittenSqlDto> Handle(WriteSqlCommand request, CancellationToken cancellationToken)
    {
        var target = await _resolver.ResolveAsync(request.DatabaseUserId, cancellationToken);

        // The names the client sent are only used to pick from what the catalog listed: nothing
        // the client sends reaches SQL as a name.
        var listed = await _cache.GetOrAddAsync(
            request.DatabaseUserId ?? Guid.Empty, () => _reader.ListObjectsAsync(target, cancellationToken), refresh: false);
        var picked = new List<SchemaObject>();
        foreach (var name in request.Objects.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var match = listed.FirstOrDefault(o => string.Equals(o.QualifiedName, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new DomainException(_messages[MessageKeys.AiUnknownTable, name]);
            picked.Add(match);
        }

        var samples = await _settings.GetBoolAsync(
            SystemSettingKeys.AiSampleValues, SystemSettingKeys.AiSampleValuesDefault, cancellationToken);
        var details = await _reader.DescribeAsync(target, picked, samples, cancellationToken);

        var houseRules = await _settings.GetStringAsync(SystemSettingKeys.AiInstructions, cancellationToken);

        var answer = await _ai.CompleteJsonAsync<WriteAnswer>(
            new AiChatRequest(
                AiPrompts.System(target.ServerType, AiPrompts.WriteTask, houseRules),
                AiPrompts.WriteUser(request.Description, details, request.ExistingParameters),
                "sql_query",
                AiPrompts.WriteSchema,
                MaxTokens: 1800),
            cancellationToken);

        // Told not to, a model still likes to end with a semicolon; Jadwal refuses one. And a
        // small one writes [TABLE.COLUMN] — in brackets that is one name with a dot in it, which
        // no catalog here holds, so it can only have meant TABLE.COLUMN.
        var sql = DottedBracketRegex().Replace(answer.Sql.Trim().TrimEnd(';').TrimEnd(), "$1.$2");
        if (sql.Length == 0)
            throw new ExternalServiceException(_messages[MessageKeys.AiBadAnswer]);

        // Its reasoning written into the SQL field is an unreadable answer, not SQL to offer.
        if (AiSqlGuard.LooksLikeProse(sql))
            throw new ExternalServiceException(_messages[MessageKeys.AiBadAnswer]);

        var problem = AiSqlGuard.FindProblem(QueryType.Select, sql);
        if (problem is not null)
            throw new DomainException(_messages[MessageKeys.AiUnsafeSql, problem]);

        var max = await _settings.GetIntAsync(
            SystemSettingKeys.QuerySqlMaxLength, SystemSettingKeys.QuerySqlMaxLengthDefault, cancellationToken);
        if (sql.Length > max)
            throw new DomainException(_messages[MessageKeys.SqlTooLong, max, sql.Length]);

        var notes = new List<string>();
        return new AiWrittenSqlDto
        {
            Sql = sql,
            Explanation = answer.Explanation.Trim(),
            Parameters = ReconcileParameters(sql, answer.Parameters, request.ExistingParameters, notes),
            Notes = notes
        };
    }

    /// <summary>
    /// The parameters the SQL really uses, in order of first use, held to how Jadwal binds them.
    /// The model's description is kept where the SQL bears it out, and corrected where it does
    /// not, with a note: optional only if the SQL handles the NULL an empty value arrives as,
    /// several values only if the SQL reads them with IN (…), a list only with its choices, and a
    /// default only if it is valid for the type. One the form already has is marked, not repeated.
    /// </summary>
    private List<AiSuggestedParameterDto> ReconcileParameters(
        string sql, List<WriteAnswerParameter> proposed, List<AiExistingParameter> existing, List<string> notes)
    {
        var stripped = QueryTypeClassifier.StripLiteralsAndComments(sql);
        var used = ParameterRegex().Matches(stripped)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => n.Length <= 100)
            .ToList();

        var result = new List<AiSuggestedParameterDto>();
        foreach (var name in used)
        {
            var known = existing.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (known is not null)
            {
                result.Add(new AiSuggestedParameterDto
                {
                    Name = known.Name, DisplayName = known.DisplayName, ParameterType = known.ParameterType,
                    IsRequired = known.IsRequired, AllowMultiple = known.AllowMultiple, AlreadyInForm = true
                });
                continue;
            }

            var p = proposed.FirstOrDefault(x => string.Equals(x.Name.TrimStart('@'), name, StringComparison.OrdinalIgnoreCase));
            var displayName = p?.DisplayName.Trim() is { Length: > 0 } d ? d : name;
            var dto = new AiSuggestedParameterDto
            {
                Name = name,
                DisplayName = displayName.Length <= 200 ? displayName : displayName[..200],
                ParameterType = p is not null && ParameterTypes.TryGetValue(p.Type, out var type) ? type : ParameterType.String,
                IsRequired = p?.Required ?? true
            };

            if (dto.ParameterType == ParameterType.Dropdown)
            {
                var options = (p?.Options ?? new())
                    .Where(o => !string.IsNullOrWhiteSpace(o.Value))
                    .GroupBy(o => o.Value.Trim(), StringComparer.Ordinal)
                    .Select(g => new { label = g.First().Label.Trim() is { Length: > 0 } l ? l : g.Key, value = g.Key })
                    .Take(MaxOptions)
                    .ToList();
                if (options.Count == 0)
                    dto.ParameterType = ParameterType.String;
                else
                    dto.DropdownStaticValues = System.Text.Json.JsonSerializer.Serialize(options);
            }

            // Several values bind as a list for IN (…) and nowhere else.
            if (p?.Multiple == true && dto.ParameterType is ParameterType.String or ParameterType.Dropdown)
            {
                if (Regex.IsMatch(stripped, $@"\bIN\s*\(\s*@{Regex.Escape(name)}\s*\)", RegexOptions.IgnoreCase))
                {
                    dto.AllowMultiple = true;
                    dto.IsRequired = true;
                }
                else
                {
                    notes.Add(_messages[MessageKeys.AiParamSingleValue, name]);
                }
            }

            // An empty value arrives as NULL; optional only if the SQL says what NULL means.
            if (!dto.IsRequired
                && !Regex.IsMatch(stripped, $@"@{Regex.Escape(name)}\s+IS\s+NULL", RegexOptions.IgnoreCase))
            {
                dto.IsRequired = true;
                notes.Add(_messages[MessageKeys.AiParamMadeRequired, name]);
            }

            dto.DefaultValue = ValidDefault(p?.DefaultValue, dto);
            result.Add(dto);
        }
        return result;
    }

    /// <summary>
    /// A default the query form can hold for this type, or null. A date gets none: a fixed date
    /// goes stale, and the run form takes no shortcut such as today-30.
    /// </summary>
    private static string? ValidDefault(string? value, AiSuggestedParameterDto p)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v) || p.AllowMultiple) return null;
        return p.ParameterType switch
        {
            ParameterType.Number => decimal.TryParse(v, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out _) ? v : null,
            ParameterType.Boolean => bool.TryParse(v, out var b) ? (b ? "true" : "false") : null,
            ParameterType.Date => null,
            ParameterType.Dropdown => p.DropdownStaticValues is { } json && json.Contains(System.Text.Json.JsonSerializer.Serialize(v)) ? v : null,
            _ => v.Length <= 500 ? v : null
        };
    }

    /// <summary><c>[A.B]</c> where both halves are ordinary identifiers.</summary>
    [GeneratedRegex(@"\[([A-Za-z_][A-Za-z0-9_$#]*)\.([A-Za-z_][A-Za-z0-9_$#]*)\]")]
    private static partial Regex DottedBracketRegex();

    /// <summary>The query form's rule for a parameter name, after the @.</summary>
    [GeneratedRegex(@"@([a-zA-Z_][a-zA-Z0-9_]*)")]
    private static partial Regex ParameterRegex();

    private sealed class WriteAnswer
    {
        public string Sql { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public List<WriteAnswerParameter> Parameters { get; set; } = new();
    }

    private sealed class WriteAnswerParameter
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool Required { get; set; } = true;
        public bool Multiple { get; set; }
        public string DefaultValue { get; set; } = string.Empty;
        public List<WriteAnswerOption> Options { get; set; } = new();
    }

    private sealed class WriteAnswerOption
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
