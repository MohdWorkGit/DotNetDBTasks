using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.Users.Commands;

public class ToggleUserActiveCommand : IRequest
{
    public Guid UserId { get; set; }
    public bool IsActive { get; set; }
}

public class ToggleUserActiveCommandHandler : IRequestHandler<ToggleUserActiveCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public ToggleUserActiveCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(ToggleUserActiveCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw new NotFoundException("User", request.UserId);

        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
