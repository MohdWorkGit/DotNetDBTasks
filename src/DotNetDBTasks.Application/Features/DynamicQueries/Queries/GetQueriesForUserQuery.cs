using AutoMapper;
using DotNetDBTasks.Application.Common.Interfaces;
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
        var queryIds = new HashSet<Guid>();

        // 1. Queries accessible via role assignments
        var userRoles = await _unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == _currentUser.UserId, cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).ToHashSet();

        var queryRoles = await _unitOfWork.DynamicQueryRoles.FindAsync(
            qr => roleIds.Contains(qr.RoleId), cancellationToken);
        foreach (var qr in queryRoles)
            queryIds.Add(qr.DynamicQueryId);

        // 2. Queries accessible via department assignments
        var department = _currentUser.Department;
        if (!string.IsNullOrEmpty(department))
        {
            var queryDepartments = await _unitOfWork.DynamicQueryDepartments.FindAsync(
                qd => qd.Department == department, cancellationToken);
            foreach (var qd in queryDepartments)
                queryIds.Add(qd.DynamicQueryId);
        }

        // 3. Queries assigned directly to this user
        var queryUsers = await _unitOfWork.DynamicQueryUsers.FindAsync(
            qu => qu.UserId == _currentUser.UserId, cancellationToken);
        foreach (var qu in queryUsers)
            queryIds.Add(qu.DynamicQueryId);

        // Fetch the matching enabled queries with related data
        var queries = await _unitOfWork.DynamicQueries.FindAsync(
            q => queryIds.Contains(q.Id) && q.IsEnabled, cancellationToken,
            "DynamicQueryRoles.Role", "DynamicQueryDepartments", "DynamicQueryUsers.User", "Parameters");

        return _mapper.Map<IReadOnlyList<DynamicQueryDto>>(queries);
    }
}
