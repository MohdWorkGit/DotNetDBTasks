using AutoMapper;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Retrieves a single enabled dynamic query by its identifier,
/// verifying the current user has access via role, department, or direct assignment.
/// </summary>
public record GetQueryForUserByIdQuery(Guid Id) : IRequest<DynamicQueryDto>;

public class GetQueryForUserByIdQueryHandler
    : IRequestHandler<GetQueryForUserByIdQuery, DynamicQueryDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;

    public GetQueryForUserByIdQueryHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<DynamicQueryDto> Handle(
        GetQueryForUserByIdQuery request,
        CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.Id, cancellationToken,
            "DynamicQueryRoles.Role", "DynamicQueryDepartments", "DynamicQueryUsers.User",
            "Parameters", "DatabaseUser",
            "QueryGroup.QueryGroupRoles", "QueryGroup.QueryGroupDepartments", "QueryGroup.QueryGroupUsers");

        if (query is null || !query.IsEnabled)
            throw new NotFoundException(nameof(Domain.Entities.DynamicQuery), request.Id);

        var userRoles = await _unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == _currentUser.UserId, cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).ToHashSet();
        var department = _currentUser.Department;

        var hasRoleAccess = query.DynamicQueryRoles.Any(qr => roleIds.Contains(qr.RoleId));
        var hasDeptAccess = !string.IsNullOrEmpty(department)
            && query.DynamicQueryDepartments.Any(qd => qd.Department == department);
        var hasDirectAccess = query.DynamicQueryUsers.Any(qu => qu.UserId == _currentUser.UserId);

        // Group-level access: any assignment on the parent group grants access to this query.
        var hasGroupAccess = query.QueryGroup is not null && (
            query.QueryGroup.QueryGroupRoles.Any(gr => roleIds.Contains(gr.RoleId)) ||
            (!string.IsNullOrEmpty(department)
                && query.QueryGroup.QueryGroupDepartments.Any(gd => gd.Department == department)) ||
            query.QueryGroup.QueryGroupUsers.Any(gu => gu.UserId == _currentUser.UserId));

        if (!hasRoleAccess && !hasDeptAccess && !hasDirectAccess && !hasGroupAccess)
            throw new ForbiddenAccessException("You do not have access to this query.");

        return _mapper.Map<DynamicQueryDto>(query);
    }
}
