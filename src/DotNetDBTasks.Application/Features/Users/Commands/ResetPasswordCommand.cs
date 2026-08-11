using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Security;
using DotNetDBTasks.Domain.Enums;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using FluentValidation;
using MediatR;

namespace DotNetDBTasks.Application.Features.Users.Commands;

public class ResetPasswordCommand : IRequest<ResetPasswordResult>
{
    public Guid UserId { get; set; }
}

public class ResetPasswordResult
{
    public string TemporaryPassword { get; set; } = string.Empty;
}

public class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, ResetPasswordResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AdminAccountGuard _adminGuard;

    public ResetPasswordCommandHandler(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        AdminAccountGuard adminGuard)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _adminGuard = adminGuard;
    }

    public async Task<ResetPasswordResult> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw new NotFoundException("User", request.UserId);

        // The result hands back the new password in clear text, so a non-Admin resetting an
        // administrator would be handing themselves that account.
        await _adminGuard.EnsureCanModifyUserAsync(request.UserId, cancellationToken);

        if (user.AuthSource == AuthSource.Ldap)
            throw new DomainException("Cannot reset password for LDAP users. Passwords are managed by Active Directory.");

        var tempPassword = GenerateTemporaryPassword();
        user.PasswordHash = _passwordHasher.HashPassword(tempPassword);
        user.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ResetPasswordResult { TemporaryPassword = tempPassword };
    }

    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%";

        var rng = new Random();
        var chars = new char[12];
        chars[0] = upper[rng.Next(upper.Length)];
        chars[1] = lower[rng.Next(lower.Length)];
        chars[2] = digits[rng.Next(digits.Length)];
        chars[3] = special[rng.Next(special.Length)];

        var all = upper + lower + digits + special;
        for (int i = 4; i < chars.Length; i++)
            chars[i] = all[rng.Next(all.Length)];

        // Shuffle
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
