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
            "DynamicQueryRoles.Role", "DynamicQueryDepartments", "DynamicQueryUsers.User", "Parameters", "DatabaseUser");

        if (query is null || !query.IsEnabled)
            throw new NotFoundException(nameof(Domain.Entities.DynamicQuery), request.Id);

        // Verify user has access via roles
        var userRoles = await _unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == _currentUser.UserId, cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).ToHashSet();
        var hasRoleAccess = query.DynamicQueryRoles.Any(qr => roleIds.Contains(qr.RoleId));

        // Verify user has access via department
        var department = _currentUser.Department;
        var hasDeptAccess = !string.IsNullOrEmpty(department)
            && query.DynamicQueryDepartments.Any(qd => qd.Department == department);

        // Verify user has direct access
        var hasDirectAccess = query.DynamicQueryUsers.Any(qu => qu.UserId == _currentUser.UserId);

        if (!hasRoleAccess && !hasDeptAccess && !hasDirectAccess)
            throw new ForbiddenAccessException("You do not have access to this query.");

        return _mapper.Map<DynamicQueryDto>(query);
    }
}
