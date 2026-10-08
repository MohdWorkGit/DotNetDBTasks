using System.Text.Json;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Exceptions;

namespace Bayan.Application.Features.QueryExecution;

/// <summary>
/// Turns the raw parameter values a user typed into the typed values a query is bound with:
/// required checks, number/date/boolean conversion, multi-value lists for IN clauses, and the
/// check that a fixed-list dropdown value is one of its options.
///
/// <para>Shared by a real run (<c>ExecuteQueryCommand</c>) and a test run from the query editor
/// (<c>TestDynamicQueryCommand</c>), so a test binds parameters exactly as the saved query will.</para>
/// </summary>
public static class QueryParameterBinder
{
    public static Dictionary<string, object?> Bind(
        IEnumerable<QueryParameter> definitions,
        IReadOnlyDictionary<string, string> values)
    {
        var typedParameters = new Dictionary<string, object?>();
        foreach (var paramDef in definitions)
        {
            values.TryGetValue(paramDef.Name, out var rawValue);

            if (paramDef.IsRequired && string.IsNullOrWhiteSpace(rawValue))
                throw new DomainException($"Parameter '{paramDef.DisplayName}' is required.");

            // AllowMultiple expands into IN-clause bind variables. Supported for Dropdown
            // (UI sends the selected values as a JSON array) and String (UI converts the
            // user's "a, b, c" textbox input into the same JSON-array wire format).
            if (paramDef.AllowMultiple)
            {
                var items = ParseMultiSelectValue(rawValue, paramDef);
                if (paramDef.IsRequired && items.Length == 0)
                    throw new DomainException($"Parameter '{paramDef.DisplayName}' is required.");
                typedParameters[paramDef.Name] = items;
                continue;
            }

            if (paramDef.ParameterType == ParameterType.Dropdown && !string.IsNullOrWhiteSpace(rawValue))
                ValidateDropdownOption(rawValue, paramDef);

            typedParameters[paramDef.Name] = ConvertParameter(rawValue, paramDef.ParameterType);
        }

        return typedParameters;
    }

    private static object? ConvertParameter(string? rawValue, ParameterType type)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return DBNull.Value;

        return type switch
        {
            ParameterType.String => rawValue.Length > 4000
                ? throw new DomainException($"Parameter value exceeds the maximum allowed length of 4000 characters.")
                : rawValue,
            ParameterType.Number => decimal.TryParse(rawValue, out var num) ? num
                : throw new DomainException($"Invalid number value: {rawValue}"),
            ParameterType.Date => DateTime.TryParse(rawValue, out var date) ? date
                : throw new DomainException($"Invalid date value: {rawValue}"),
            // Oracle NUMBER(1) columns require 0/1 integers, not .NET bool values
            ParameterType.Boolean => bool.TryParse(rawValue, out var flag) ? (flag ? 1 : 0)
                : throw new DomainException($"Invalid boolean value: {rawValue}"),
            // Dropdown value is passed as a plain string (the selected option's value)
            ParameterType.Dropdown => rawValue,
            _ => rawValue
        };
    }

    private static void ValidateDropdownOption(string rawValue, QueryParameter paramDef)
    {
        var allowed = GetStaticDropdownAllowedValues(paramDef);
        if (allowed is null) return;

        if (!allowed.Contains(rawValue))
            throw new DomainException($"Invalid value for parameter '{paramDef.DisplayName}'. Select a valid option.");
    }

    /// <summary>
    /// Parses the JSON-array payload sent for a MultiSelect parameter and returns the
    /// individual string values. For static dropdowns, each value is verified against
    /// the configured option list. Empty/null input yields an empty array (caller
    /// enforces the required check separately).
    /// </summary>
    private static string[] ParseMultiSelectValue(string? rawValue, QueryParameter paramDef)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return Array.Empty<string>();

        string[] items;
        try
        {
            using var doc = JsonDocument.Parse(rawValue);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new DomainException($"Parameter '{paramDef.DisplayName}' must be a JSON array of values.");

            items = doc.RootElement.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .ToArray();
        }
        catch (JsonException)
        {
            throw new DomainException($"Parameter '{paramDef.DisplayName}' must be a JSON array of values.");
        }

        var allowed = GetStaticDropdownAllowedValues(paramDef);
        if (allowed is not null)
        {
            foreach (var item in items)
            {
                if (!allowed.Contains(item))
                    throw new DomainException($"Invalid value for parameter '{paramDef.DisplayName}'. Select a valid option.");
            }
        }

        return items;
    }

    private static HashSet<string>? GetStaticDropdownAllowedValues(QueryParameter paramDef)
    {
        if (paramDef.DropdownSourceType != DropdownSourceType.Static
            || string.IsNullOrWhiteSpace(paramDef.DropdownStaticValues))
            return null;

        using var doc = JsonDocument.Parse(paramDef.DropdownStaticValues);
        return doc.RootElement.EnumerateArray()
            .Select(e => e.TryGetProperty("value", out var v) ? v.GetString() : null)
            .Where(v => v is not null)
            .Select(v => v!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
