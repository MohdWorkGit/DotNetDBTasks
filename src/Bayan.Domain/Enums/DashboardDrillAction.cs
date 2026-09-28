namespace Bayan.Domain.Enums;

/// <summary>What clicking a category on a dashboard tile does.</summary>
public enum DashboardDrillAction
{
    None = 0,

    /// <summary>Sets one of the dashboard's filters to the clicked category, re-querying every tile.</summary>
    FilterDashboard = 1,

    /// <summary>Opens a report with the clicked category passed as one of its parameters.</summary>
    OpenReport = 2,

    /// <summary>Opens the tile's underlying rows in a grid.</summary>
    ShowRows = 3
}
