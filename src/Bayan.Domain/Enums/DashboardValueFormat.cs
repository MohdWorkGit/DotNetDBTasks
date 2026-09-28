namespace Bayan.Domain.Enums;

/// <summary>How a KPI tile prints its number. Formatting happens in the browser, in the viewer's locale.</summary>
public enum DashboardValueFormat
{
    Number = 0,

    /// <summary>The value is a fraction: 0.25 prints as 25%.</summary>
    Percent = 1,

    Currency = 2
}
