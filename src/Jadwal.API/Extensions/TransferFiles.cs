using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Jadwal.API.Extensions;

/// <summary>
/// How every export file is written and every import file read — queries, reports and
/// dashboards alike — so a file from one endpoint is byte-for-byte the shape another expects.
/// </summary>
public static class TransferFiles
{
    /// <summary>Largest import accepted; a backup with Word templates in it is the big case.</summary>
    public const int MaxImportBytes = 50 * 1024 * 1024;

    /// <summary>
    /// Indented on purpose: an export is something an admin may read, diff or keep in source
    /// control, not just feed back into the import endpoint.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static FileContentResult ToFile<T>(T file, string fileName) =>
        new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(file, JsonOptions)), "application/json")
        {
            FileDownloadName = fileName
        };

    /// <summary>Makes a name safe to use as a download file name.</summary>
    public static string Slug(string name, string fallback)
    {
        var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
        while (cleaned.Contains("--"))
            cleaned = cleaned.Replace("--", "-");
        cleaned = cleaned.Trim('-');
        return cleaned.Length == 0 ? fallback : cleaned;
    }
}
