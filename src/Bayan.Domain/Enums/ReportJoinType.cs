namespace Bayan.Domain.Enums;

/// <summary>
/// How a <see cref="ReportDatasetSourceType.Join"/> dataset combines its two inputs.
/// Deliberately limited to the two joins that are unambiguous to evaluate in memory
/// and to explain in the UI.
/// </summary>
public enum ReportJoinType
{
    /// <summary>Keep only left rows that have a matching right row.</summary>
    Inner = 0,

    /// <summary>Keep every left row; unmatched rows get null values for the right columns.</summary>
    Left = 1
}
