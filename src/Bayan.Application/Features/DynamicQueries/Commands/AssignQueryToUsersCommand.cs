using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Assigns a dynamic query to specific individual users.
/// </summary>
public class AssignQueryToUsersCommand : IRequest<Unit>
{
    public Guid QueryId { get; set; }
    public List<Guid> UserIds { get; set; } = new();
}

public class AssignQueryToUsersCommandHandler : IRequestHandler<AssignQueryToUsersCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignQueryToUsersCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(AssignQueryToUsersCommand request, CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.QueryId, cancellationToken);
        if (query is null)
            throw new NotFoundException(nameof(DynamicQuery), request.QueryId);

        // Remove existing user assignments
        var existing = await _unitOfWork.DynamicQueryUsers.FindAsync(
            qu => qu.DynamicQueryId == request.QueryId, cancellationToken);
        foreach (var qu in existing)
            _unitOfWork.DynamicQueryUsers.Delete(qu);

        // Add new assignments
        foreach (var userId in request.UserIds)
        {
            await _unitOfWork.DynamicQueryUsers.AddAsync(new DynamicQueryUser
            {
                DynamicQueryId = request.QueryId,
                UserId = userId
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
