namespace Bayan.Domain.Enums;

/// <summary>Where a tile's query parameter takes its value from.</summary>
public enum DashboardTileParameterSource
{
    /// <summary>One of the dashboard's filters.</summary>
    Filter = 0,

    /// <summary>A fixed value the viewer is never asked for.</summary>
    Constant = 1
}
