using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DatabaseUsers.Queries;

public class GetAllDatabaseUsersQuery : IRequest<List<DatabaseUserDto>> { }

public class GetAllDatabaseUsersQueryHandler
    : IRequestHandler<GetAllDatabaseUsersQuery, List<DatabaseUserDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetAllDatabaseUsersQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<List<DatabaseUserDto>> Handle(
        GetAllDatabaseUsersQuery request,
        CancellationToken cancellationToken)
    {
        var dbUsers = await _unitOfWork.DatabaseUsers.GetAllAsync(
            cancellationToken, "RoleAccess");

        var result = new List<DatabaseUserDto>();
        foreach (var du in dbUsers)
        {
            var dto = new DatabaseUserDto
            {
                Id = du.Id,
                Name = du.Name,
                Description = du.Description,
                ServerType = du.ServerType,
                Host = du.Host,
                Port = du.Port,
                ServiceName = du.ServiceName ?? string.Empty,
                DatabaseName = du.DatabaseName ?? string.Empty,
                DbUsername = du.DbUsername,
                IsActive = du.IsActive,
                CreatedAt = du.CreatedAt,
                AssignedRoles = new List<DatabaseUserRoleAccessDto>()
            };

            if (du.RoleAccess != null)
            {
                foreach (var access in du.RoleAccess)
                {
                    var role = await _unitOfWork.Roles.GetByIdAsync(access.RoleId, cancellationToken);
                    dto.AssignedRoles.Add(new DatabaseUserRoleAccessDto
                    {
                        RoleId = access.RoleId,
                        RoleName = role?.Name ?? string.Empty
                    });
                }
            }

            result.Add(dto);
        }

        return result;
    }
}
