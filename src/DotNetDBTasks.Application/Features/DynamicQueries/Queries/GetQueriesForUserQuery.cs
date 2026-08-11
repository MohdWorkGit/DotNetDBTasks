using AutoMapper;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Security;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Retrieves all enabled dynamic queries accessible to the current user
/// via role assignments, department assignments, or direct user assignments.
/// </summary>
public record GetQueriesForUserQuery : IRequest<IReadOnlyList<DynamicQueryDto>>;

public class GetQueriesForUserQueryHandler
    : IRequestHandler<GetQueriesForUserQuery, IReadOnlyList<DynamicQueryDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;

    public GetQueriesForUserQueryHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<DynamicQueryDto>> Handle(
        GetQueriesForUserQuery request,
        CancellationToken cancellationToken)
    {
        var roleIds = await QueryAccessRoles.GrantingRoleIdsAsync(
            _unitOfWork, _currentUser.UserId, cancellationToken);
        var department = _currentUser.Department;

        // Direct (query-level) access — role, department, or per-user assignment on the query itself.
        var queryIds = new HashSet<Guid>();

        var queryRoles = await _unitOfWork.DynamicQueryRoles.FindAsync(
            qr => roleIds.Contains(qr.RoleId), cancellationToken);
        foreach (var qr in queryRoles)
            queryIds.Add(qr.DynamicQueryId);

        if (!string.IsNullOrEmpty(department))
        {
            var queryDepartments = await _unitOfWork.DynamicQueryDepartments.FindAsync(
                qd => qd.Department == department, cancellationToken);
            foreach (var qd in queryDepartments)
                queryIds.Add(qd.DynamicQueryId);
        }

        var queryUsers = await _unitOfWork.DynamicQueryUsers.FindAsync(
            qu => qu.UserId == _currentUser.UserId, cancellationToken);
        foreach (var qu in queryUsers)
            queryIds.Add(qu.DynamicQueryId);

        // Group-level access — every query inside the group becomes visible.
        var groupIds = new HashSet<Guid>();

        var groupRoles = await _unitOfWork.QueryGroupRoles.FindAsync(
            gr => roleIds.Contains(gr.RoleId), cancellationToken);
        foreach (var gr in groupRoles) groupIds.Add(gr.QueryGroupId);

        if (!string.IsNullOrEmpty(department))
        {
            var groupDepartments = await _unitOfWork.QueryGroupDepartments.FindAsync(
                gd => gd.Department == department, cancellationToken);
            foreach (var gd in groupDepartments) groupIds.Add(gd.QueryGroupId);
        }

        var groupUsers = await _unitOfWork.QueryGroupUsers.FindAsync(
            gu => gu.UserId == _currentUser.UserId, cancellationToken);
        foreach (var gu in groupUsers) groupIds.Add(gu.QueryGroupId);

        var queries = await _unitOfWork.DynamicQueries.FindAsync(
            q => q.IsEnabled && (
                queryIds.Contains(q.Id) ||
                (q.QueryGroupId.HasValue && groupIds.Contains(q.QueryGroupId.Value))),
            cancellationToken,
            "DynamicQueryRoles.Role", "DynamicQueryDepartments", "DynamicQueryUsers.User",
            "Parameters", "DatabaseUser", "QueryGroup");

        return _mapper.Map<IReadOnlyList<DynamicQueryDto>>(queries);
    }
}
