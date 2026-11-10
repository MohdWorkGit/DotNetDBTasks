using Jadwal.Domain.Entities;
using Jadwal.Domain.Exceptions;
using Jadwal.Domain.Interfaces;

namespace Jadwal.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Files a query in exactly the groups it is given, shared by create and update so both treat
/// the list the same way: duplicates collapse, an unknown group is a 404 rather than a foreign-key
/// failure, and an empty list leaves the query ungrouped.
/// </summary>
internal static class QueryGroupFiling
{
    public static async Task ApplyAsync(
        DynamicQuery query,
        IEnumerable<Guid>? groupIds,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var wanted = (groupIds ?? Enumerable.Empty<Guid>()).Distinct().ToList();

        // Loaded rather than merely checked, so the new filings pick the group up as their
        // navigation and the DTO returned after the save carries the group names.
        var groups = wanted.Count == 0
            ? new Dictionary<Guid, QueryGroup>()
            : (await unitOfWork.QueryGroups.FindAsync(g => wanted.Contains(g.Id), cancellationToken))
                .ToDictionary(g => g.Id);
        var missing = wanted.FirstOrDefault(id => !groups.ContainsKey(id));
        if (missing != Guid.Empty)
            throw new NotFoundException(nameof(QueryGroup), missing);

        // Diffed rather than cleared and re-added: removing and re-adding the same composite key
        // in one unit of work is a tracking conflict waiting to happen.
        var existing = query.Id == Guid.Empty
            ? new List<QueryGroupQuery>()
            : (await unitOfWork.QueryGroupQueries.FindAsync(
                m => m.DynamicQueryId == query.Id, cancellationToken)).ToList();

        foreach (var filing in existing.Where(m => !groups.ContainsKey(m.QueryGroupId)))
            unitOfWork.QueryGroupQueries.Delete(filing);

        // Added through the repository, not the collection: the update handler then calls
        // Update() on the query, which would attach an untracked filing as Modified — an UPDATE
        // of a row that does not exist yet.
        var kept = existing.Select(m => m.QueryGroupId).ToHashSet();
        foreach (var group in groups.Values.Where(g => !kept.Contains(g.Id)))
        {
            await unitOfWork.QueryGroupQueries.AddAsync(new QueryGroupQuery
            {
                QueryGroupId = group.Id,
                QueryGroup = group,
                DynamicQueryId = query.Id,
                DynamicQuery = query
            }, cancellationToken);
        }
    }
}
