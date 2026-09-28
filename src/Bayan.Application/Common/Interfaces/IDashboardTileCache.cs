using Bayan.Application.Features.Dashboards.Dtos;

namespace Bayan.Application.Common.Interfaces;

/// <summary>
/// Shares a dashboard tile's result between everyone looking at it. Without it, fifty people
/// with the same dashboard open would each run every tile's query on every refresh; with it, a
/// tile runs once per interval per distinct set of filter values, whoever is watching.
///
/// <para>Holds results only. Access is <b>not</b> its concern: the caller checks the viewer's
/// grants on the dashboard and on the tile's query before asking, hit or miss.</para>
/// </summary>
public interface IDashboardTileCache
{
    /// <summary>
    /// Returns the cached result for <paramref name="key"/> while it is fresh, otherwise runs
    /// <paramref name="factory"/> — once, however many callers ask at the same moment — and keeps
    /// what it returns for as long as the result's own <c>NextRefreshAt</c> says.
    /// </summary>
    Task<DashboardTileDataDto> GetOrCreateAsync(
        Guid dashboardId,
        string key,
        Func<Task<DashboardTileDataDto>> factory);

    /// <summary>Drops every cached tile of a dashboard, so an edit shows on the very next poll.</summary>
    void InvalidateDashboard(Guid dashboardId);
}
