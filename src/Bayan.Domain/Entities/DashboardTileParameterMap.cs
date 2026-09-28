using Bayan.Domain.Enums;

namespace Bayan.Domain.Entities;

/// <summary>
/// Supplies one value to one of a tile query's declared parameters. A parameter with no map falls
/// back to the query's own default, so a tile never fails merely because it was not fully wired.
/// </summary>
public class DashboardTileParameterMap : BaseEntity
{
    public Guid DashboardTileId { get; set; }
    public DashboardTile DashboardTile { get; set; } = null!;

    /// <summary>Name of the target query's declared parameter, without the leading @.</summary>
    public string TargetParameterName { get; set; } = string.Empty;

    public DashboardTileParameterSource SourceKind { get; set; }

    /// <summary>
    /// The filter feeding this parameter, when <see cref="SourceKind"/> is
    /// <see cref="DashboardTileParameterSource.Filter"/>. No FK: the dashboard owns both sides and
    /// rewrites them together on save, and a second cascade path into this table is one Oracle rejects.
    /// </summary>
    public Guid? DashboardFilterId { get; set; }

    public string? ConstantValue { get; set; }
}
