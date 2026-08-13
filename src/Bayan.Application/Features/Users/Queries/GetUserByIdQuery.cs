using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.Users.Queries;

public record GetUserByIdQuery(Guid Id) : IRequest<UserDto>;

public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetUserByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<UserDto> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(request.Id, cancellationToken, "UserRoles");
        if (user is null)
            throw new NotFoundException("User", request.Id);

        var allRoles = await _unitOfWork.Roles.GetAllAsync(cancellationToken);
        var roleMap = allRoles.ToDictionary(r => r.Id, r => r.Name);

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
            UpdatedAt = user.UpdatedAt,
            Roles = user.UserRoles
                .Where(ur => roleMap.ContainsKey(ur.RoleId))
                .Select(ur => roleMap[ur.RoleId])
                .ToList()
        };
    }
}
