using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.QueryGroups.Commands;

/// <summary>
/// Replaces the role assignments on a query group. Users in those roles gain access
/// to every query inside the group.
/// </summary>
public class AssignQueryGroupToRolesCommand : IRequest<Unit>
{
    public Guid GroupId { get; set; }
    public List<Guid> RoleIds { get; set; } = new();
}

public class AssignQueryGroupToRolesCommandHandler : IRequestHandler<AssignQueryGroupToRolesCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignQueryGroupToRolesCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(AssignQueryGroupToRolesCommand request, CancellationToken cancellationToken)
    {
        var group = await _unitOfWork.QueryGroups.GetByIdAsync(request.GroupId, cancellationToken);
        if (group is null)
            throw new NotFoundException(nameof(QueryGroup), request.GroupId);

        var existing = await _unitOfWork.QueryGroupRoles.FindAsync(
            gr => gr.QueryGroupId == request.GroupId, cancellationToken);
        foreach (var gr in existing)
            _unitOfWork.QueryGroupRoles.Delete(gr);

        foreach (var roleId in request.RoleIds)
        {
            await _unitOfWork.QueryGroupRoles.AddAsync(new QueryGroupRole
            {
                QueryGroupId = request.GroupId,
                RoleId = roleId
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
