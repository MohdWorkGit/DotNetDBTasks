using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.QueryGroups.Commands;

public class AssignQueryGroupToUsersCommand : IRequest<Unit>
{
    public Guid GroupId { get; set; }
    public List<Guid> UserIds { get; set; } = new();
}

public class AssignQueryGroupToUsersCommandHandler : IRequestHandler<AssignQueryGroupToUsersCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignQueryGroupToUsersCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(AssignQueryGroupToUsersCommand request, CancellationToken cancellationToken)
    {
        var group = await _unitOfWork.QueryGroups.GetByIdAsync(request.GroupId, cancellationToken);
        if (group is null)
            throw new NotFoundException(nameof(QueryGroup), request.GroupId);

        var existing = await _unitOfWork.QueryGroupUsers.FindAsync(
            gu => gu.QueryGroupId == request.GroupId, cancellationToken);
        foreach (var gu in existing)
            _unitOfWork.QueryGroupUsers.Delete(gu);

        foreach (var userId in request.UserIds)
        {
            await _unitOfWork.QueryGroupUsers.AddAsync(new QueryGroupUser
            {
                QueryGroupId = request.GroupId,
                UserId = userId
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
