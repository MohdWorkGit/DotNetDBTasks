namespace Bayan.Domain.Entities;

/// <summary>
/// A page of tiles that keep themselves current: each tile runs one saved query on its own
/// refresh interval and draws the result as a KPI, a chart or a small table.
///
/// <para>Access mirrors reports — by role, by user group, or by name — and, like a report, is
/// only the <b>outer</b> gate: every tile's query is checked against the viewer independently, so
/// a dashboard can never become a way to see data from a query the viewer could not run.</para>
/// </summary>
public class Dashboard : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Nullable because Oracle stores an empty string as NULL.</summary>
    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;

    public Guid CreatedByUserId { get; set; }

    /// <summary>
    /// How often a tile refreshes when it does not set its own interval. Also how long its result
    /// is shared between viewers, so this is what bounds the load a dashboard puts on the database.
    /// </summary>
    public int DefaultRefreshSeconds { get; set; } = 60;

    public int SortOrder { get; set; }

    public ICollection<DashboardTile> Tiles { get; set; } = new List<DashboardTile>();
    public ICollection<DashboardFilter> Filters { get; set; } = new List<DashboardFilter>();
    public ICollection<DashboardRole> DashboardRoles { get; set; } = new List<DashboardRole>();
    public ICollection<DashboardUserGroup> DashboardUserGroups { get; set; } = new List<DashboardUserGroup>();
    public ICollection<DashboardUser> DashboardUsers { get; set; } = new List<DashboardUser>();
}
