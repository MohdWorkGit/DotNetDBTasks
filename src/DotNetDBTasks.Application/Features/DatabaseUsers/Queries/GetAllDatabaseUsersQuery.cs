using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DatabaseUsers.Queries;

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
            cancellationToken, "UserAccess");

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
                AssignedUsers = new List<DatabaseUserAccessDto>()
            };

            if (du.UserAccess != null)
            {
                foreach (var access in du.UserAccess)
                {
                    var user = await _unitOfWork.Users.GetByIdAsync(access.UserId, cancellationToken);
                    dto.AssignedUsers.Add(new DatabaseUserAccessDto
                    {
                        UserId = access.UserId,
                        Username = user?.Username ?? string.Empty
                    });
                }
            }

            result.Add(dto);
        }

        return result;
    }
}
