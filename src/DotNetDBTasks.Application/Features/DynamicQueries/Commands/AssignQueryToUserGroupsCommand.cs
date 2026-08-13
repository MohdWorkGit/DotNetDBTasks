using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Assigns a dynamic query to one or more user groups.
/// Every member of those groups gains access to the query.
/// </summary>
public class AssignQueryToUserGroupsCommand : IRequest<Unit>
{
    public Guid QueryId { get; set; }
    public List<Guid> UserGroupIds { get; set; } = new();
}

public class AssignQueryToUserGroupsCommandHandler : IRequestHandler<AssignQueryToUserGroupsCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignQueryToUserGroupsCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(AssignQueryToUserGroupsCommand request, CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.QueryId, cancellationToken);
        if (query is null)
            throw new NotFoundException(nameof(DynamicQuery), request.QueryId);

        // Remove existing user group assignments
        var existing = await _unitOfWork.DynamicQueryUserGroups.FindAsync(
            qg => qg.DynamicQueryId == request.QueryId, cancellationToken);
        foreach (var qg in existing)
            _unitOfWork.DynamicQueryUserGroups.Delete(qg);

        // Add new assignments
        foreach (var userGroupId in request.UserGroupIds.Distinct())
        {
            await _unitOfWork.DynamicQueryUserGroups.AddAsync(new DynamicQueryUserGroup
            {
                DynamicQueryId = request.QueryId,
                UserGroupId = userGroupId
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
