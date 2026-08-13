using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Assigns a dynamic query to one or more roles.
/// </summary>
public class AssignQueryToRolesCommand : IRequest<Unit>
{
    public Guid QueryId { get; set; }
    public List<Guid> RoleIds { get; set; } = new();
}

public class AssignQueryToRolesCommandHandler : IRequestHandler<AssignQueryToRolesCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public AssignQueryToRolesCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(AssignQueryToRolesCommand request, CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.QueryId, cancellationToken);
        if (query is null)
            throw new NotFoundException(nameof(DynamicQuery), request.QueryId);

        // Remove existing role assignments
        var existing = await _unitOfWork.DynamicQueryRoles.FindAsync(
            qr => qr.DynamicQueryId == request.QueryId, cancellationToken);
        foreach (var qr in existing)
            _unitOfWork.DynamicQueryRoles.Delete(qr);

        // Add new assignments
        foreach (var roleId in request.RoleIds)
        {
            await _unitOfWork.DynamicQueryRoles.AddAsync(new DynamicQueryRole
            {
                DynamicQueryId = request.QueryId,
                RoleId = roleId
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
