using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Application.Features.Users.Queries;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Enums;
using Bayan.Domain.Interfaces;
using FluentValidation;
using MediatR;

namespace Bayan.Application.Features.Users.Commands;

public class CreateUserCommand : IRequest<UserDto>
{
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public List<Guid> RoleIds { get; set; } = new();
}

public class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(100);
        // Optional. Only checked for shape when the caller actually supplied one.
        RuleFor(x => x.Email)
            .EmailAddress().MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6).MaximumLength(100);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoleIds).NotEmpty().WithMessage("At least one role is required.");
    }
}

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AdminAccountGuard _adminGuard;
    private readonly IAppLocalizer _messages;

    public CreateUserCommandHandler(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        AdminAccountGuard adminGuard,
        IAppLocalizer messages)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _adminGuard = adminGuard;
        _messages = messages;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        // Creating a brand-new administrator (whose password the caller chooses) is the same
        // escalation as promoting an existing account.
        await _adminGuard.EnsureCanAssignRolesAsync(request.RoleIds, cancellationToken);

        var exists = await _unitOfWork.Users.ExistsAsync(
            u => u.Username == request.Username, cancellationToken);
        if (exists)
            throw new DomainException(_messages[MessageKeys.UsernameTaken, request.Username]);

        // "" would violate the unique index on the second address-less user (Oracle stores
        // it as NULL on the way in but the in-memory comparison above would not catch it),
        // so normalize blank to null and skip the uniqueness check entirely.
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

        if (email is not null)
        {
            var emailExists = await _unitOfWork.Users.ExistsAsync(
                u => u.Email == email, cancellationToken);
            if (emailExists)
                throw new DomainException(_messages[MessageKeys.EmailInUse, email]);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            Email = email,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            AuthSource = AuthSource.Local,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Users.AddAsync(user, cancellationToken);

        var allRoles = await _unitOfWork.Roles.GetAllAsync(cancellationToken);
        var roleMap = allRoles.ToDictionary(r => r.Id, r => r.Name);
        var roleNames = new List<string>();

        foreach (var roleId in request.RoleIds)
        {
            if (!roleMap.ContainsKey(roleId))
                throw new DomainException($"Role with ID '{roleId}' does not exist.");

            await _unitOfWork.UserRoles.AddAsync(new UserRole
            {
                UserId = user.Id,
                RoleId = roleId
            }, cancellationToken);

            roleNames.Add(roleMap[roleId]);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            IsActive = user.IsActive,
            AuthSource = user.AuthSource.ToString(),
            Department = user.Department,
            CreatedAt = user.CreatedAt,
            Roles = roleNames
        };
    }
}
