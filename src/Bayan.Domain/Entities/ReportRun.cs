namespace Bayan.Domain.Entities;

/// <summary>
/// Audit row for one report execution — the report-level counterpart to
/// <see cref="QueryExecutionLog"/>, which still records each individual dataset
/// execution underneath. Both are written: the per-query log keeps the existing
/// oversight intact, and this row ties those executions together into the run the
/// user actually asked for.
/// </summary>
public class ReportRun : BaseEntity
{
    public Guid ReportId { get; set; }
    public Report Report { get; set; } = null!;

    public Guid UserId { get; set; }

    /// <summary>JSON object of the report-level parameter values the run used.</summary>
    public string? ParametersJson { get; set; }

    public DateTime StartedAt { get; set; }

    public long DurationMs { get; set; }

    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// JSON array of per-dataset outcomes — key, display name, row count and error —
    /// so a partially failed run says which section is missing rather than failing whole.
    /// </summary>
    public string? DatasetResultsJson { get; set; }
}
