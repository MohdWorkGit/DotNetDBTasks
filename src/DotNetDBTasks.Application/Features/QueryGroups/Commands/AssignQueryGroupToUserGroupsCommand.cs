using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.QueryGroups.Commands;

/// <summary>
/// Assigns a query group to one or more user groups. Every member of those user groups
/// gains access to every query inside the query group.
/// </summary>
public class AssignQueryGroupToUserGroupsCommand : IRequest<Unit>
{
    public Guid GroupId { get; set; }
    public List<Guid> UserGroupIds { get; set; } = new();
}

public class AssignQueryGroupToUserGroupsCommandHandler : IRequestHandler<AssignQueryGroupToUserGroupsCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignQueryGroupToUserGroupsCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(AssignQueryGroupToUserGroupsCommand request, CancellationToken cancellationToken)
    {
        var group = await _unitOfWork.QueryGroups.GetByIdAsync(request.GroupId, cancellationToken);
        if (group is null)
            throw new NotFoundException(nameof(QueryGroup), request.GroupId);

        var existing = await _unitOfWork.QueryGroupUserGroups.FindAsync(
            gg => gg.QueryGroupId == request.GroupId, cancellationToken);
        foreach (var gg in existing)
            _unitOfWork.QueryGroupUserGroups.Delete(gg);

        foreach (var userGroupId in request.UserGroupIds.Distinct())
        {
            await _unitOfWork.QueryGroupUserGroups.AddAsync(new QueryGroupUserGroup
            {
                QueryGroupId = request.GroupId,
                UserGroupId = userGroupId
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
