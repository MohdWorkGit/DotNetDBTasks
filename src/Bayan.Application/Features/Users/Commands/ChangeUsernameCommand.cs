using Bayan.Application.Common.Security;
using Bayan.Domain.Enums;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Bayan.Application.Common.Interfaces;

namespace Bayan.Application.Features.Users.Commands;

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
    private readonly AdminAccountGuard _adminGuard;
    private readonly IAppLocalizer _messages;

    public ChangeUsernameCommandHandler(IUnitOfWork unitOfWork, AdminAccountGuard adminGuard, IAppLocalizer messages)
    {
        _unitOfWork = unitOfWork;
        _adminGuard = adminGuard;
        _messages = messages;
    }

    public async Task Handle(ChangeUsernameCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw new NotFoundException("User", request.UserId);

        await _adminGuard.EnsureCanModifyUserAsync(request.UserId, cancellationToken);

        if (user.AuthSource == AuthSource.Ldap)
            throw new DomainException(_messages[MessageKeys.LdapUsernameImmutable]);

        var taken = await _unitOfWork.Users.ExistsAsync(
            u => u.Username == request.NewUsername && u.Id != request.UserId, cancellationToken);
        if (taken)
            throw new DomainException(_messages[MessageKeys.UsernameTaken, request.NewUsername]);

        user.Username = request.NewUsername;
        user.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
