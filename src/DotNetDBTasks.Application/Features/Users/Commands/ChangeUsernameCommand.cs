using DotNetDBTasks.Domain.Enums;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using FluentValidation;
using MediatR;

namespace DotNetDBTasks.Application.Features.Users.Commands;

public class ChangeUsernameCommand : IRequest
{
    public Guid UserId { get; set; }
    public string NewUsername { get; set; } = string.Empty;
}

public class ChangeUsernameValidator : AbstractValidator<ChangeUsernameCommand>
{
    public ChangeUsernameValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NewUsername).NotEmpty().MaximumLength(100);
    }
}

public class ChangeUsernameCommandHandler : IRequestHandler<ChangeUsernameCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public ChangeUsernameCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(ChangeUsernameCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw new NotFoundException("User", request.UserId);

        if (user.AuthSource == AuthSource.Ldap)
            throw new InvalidOperationException("Cannot change username for LDAP users. Usernames are managed by Active Directory.");

        var taken = await _unitOfWork.Users.ExistsAsync(
            u => u.Username == request.NewUsername && u.Id != request.UserId, cancellationToken);
        if (taken)
            throw new InvalidOperationException($"Username '{request.NewUsername}' is already taken.");

        user.Username = request.NewUsername;
        user.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
