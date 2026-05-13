using AutoMapper;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Features.DynamicQueries.Queries;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.QueryGroups.Queries;

/// <summary>
/// Builds the "My Queries" view, grouped by QueryGroup. A user sees:
///   - every query they have direct/role/department access to, and
///   - every query inside a group they have direct/role/department access to.
/// Queries with no group are bucketed into a synthetic "Ungrouped" entry (Id=null).
/// Empty groups are omitted.
/// </summary>
public record GetMyQueryGroupsQuery : IRequest<IReadOnlyList<MyQueryGroupDto>>;

public class GetMyQueryGroupsQueryHandler
    : IRequestHandler<GetMyQueryGroupsQuery, IReadOnlyList<MyQueryGroupDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;

    public GetMyQueryGroupsQueryHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<MyQueryGroupDto>> Handle(
        GetMyQueryGroupsQuery request,
        CancellationToken cancellationToken)
    {
        var userRoles = await _unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == _currentUser.UserId, cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).ToHashSet();
        var department = _currentUser.Department;

        // 1. Resolve query-level access (role / department / direct user).
        var accessibleQueryIds = new HashSet<Guid>();

        var queryRoles = await _unitOfWork.DynamicQueryRoles.FindAsync(
            qr => roleIds.Contains(qr.RoleId), cancellationToken);
        foreach (var qr in queryRoles) accessibleQueryIds.Add(qr.DynamicQueryId);

        if (!string.IsNullOrEmpty(department))
        {
            var queryDepartments = await _unitOfWork.DynamicQueryDepartments.FindAsync(
                qd => qd.Department == department, cancellationToken);
            foreach (var qd in queryDepartments) accessibleQueryIds.Add(qd.DynamicQueryId);
        }

        var queryUsers = await _unitOfWork.DynamicQueryUsers.FindAsync(
            qu => qu.UserId == _currentUser.UserId, cancellationToken);
        foreach (var qu in queryUsers) accessibleQueryIds.Add(qu.DynamicQueryId);

        // 2. Resolve group-level access — every query inside an accessible group is visible.
        var accessibleGroupIds = new HashSet<Guid>();

        var groupRoles = await _unitOfWork.QueryGroupRoles.FindAsync(
            gr => roleIds.Contains(gr.RoleId), cancellationToken);
        foreach (var gr in groupRoles) accessibleGroupIds.Add(gr.QueryGroupId);

        if (!string.IsNullOrEmpty(department))
        {
            var groupDepartments = await _unitOfWork.QueryGroupDepartments.FindAsync(
                gd => gd.Department == department, cancellationToken);
            foreach (var gd in groupDepartments) accessibleGroupIds.Add(gd.QueryGroupId);
        }

        var groupUsers = await _unitOfWork.QueryGroupUsers.FindAsync(
            gu => gu.UserId == _currentUser.UserId, cancellationToken);
        foreach (var gu in groupUsers) accessibleGroupIds.Add(gu.QueryGroupId);

        // 3. Pull the queries — those directly accessible OR inside an accessible group.
        var queries = await _unitOfWork.DynamicQueries.FindAsync(
            q => q.IsEnabled && (
                accessibleQueryIds.Contains(q.Id) ||
                (q.QueryGroupId.HasValue && accessibleGroupIds.Contains(q.QueryGroupId.Value))),
            cancellationToken,
            "DynamicQueryRoles.Role", "DynamicQueryDepartments", "DynamicQueryUsers.User",
            "Parameters", "DatabaseUser", "QueryGroup");

        // 4. Bucket queries by group, ordering groups by name and "Ungrouped" last.
        var groupedByGroup = queries
            .GroupBy(q => q.QueryGroupId)
            .Select(g =>
            {
                var first = g.First();
                return new MyQueryGroupDto
                {
                    Id = g.Key,
                    Name = g.Key.HasValue ? first.QueryGroup?.Name ?? "Unknown" : "Ungrouped",
                    Description = g.Key.HasValue ? first.QueryGroup?.Description ?? string.Empty : string.Empty,
                    Queries = _mapper.Map<List<DynamicQueryDto>>(g.OrderBy(q => q.Name).ToList())
                };
            })
            .OrderBy(g => g.Id.HasValue ? 0 : 1)
            .ThenBy(g => g.Name)
            .ToList();

        return groupedByGroup;
    }
}
