namespace Bayan.Domain.Enums;

/// <summary>
/// How a KPI tile reduces its query's rows to one number.
/// </summary>
public enum DashboardKpiAggregate
{
    /// <summary>
    /// The rows are a series in query order: the headline is the last row, the comparison the
    /// compare column on that row or else the row before it.
    /// </summary>
    Last = 0,

    Sum = 1,

    Average = 2,

    /// <summary>The number of rows returned; the value column is not read.</summary>
    Count = 3,

    Min = 4,

    Max = 5
}
