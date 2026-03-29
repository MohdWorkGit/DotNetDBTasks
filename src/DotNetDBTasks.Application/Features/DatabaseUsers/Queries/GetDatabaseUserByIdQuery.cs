using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DatabaseUsers.Queries;

public class GetDatabaseUserByIdQuery : IRequest<DatabaseUserDto>
{
    public Guid Id { get; set; }
    public GetDatabaseUserByIdQuery(Guid id) => Id = id;
}

public class GetDatabaseUserByIdQueryHandler
    : IRequestHandler<GetDatabaseUserByIdQuery, DatabaseUserDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetDatabaseUserByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<DatabaseUserDto> Handle(
        GetDatabaseUserByIdQuery request,
        CancellationToken cancellationToken)
    {
        var du = await _unitOfWork.DatabaseUsers.GetByIdAsync(request.Id, cancellationToken, "UserAccess");
        if (du is null)
            throw new NotFoundException("DatabaseUser", request.Id);

        var dto = new DatabaseUserDto
        {
            Id = du.Id,
            Name = du.Name,
            Description = du.Description,
            ServerType = du.ServerType,
            Host = du.Host,
            Port = du.Port,
            ServiceName = du.ServiceName,
            DatabaseName = du.DatabaseName,
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

        return dto;
    }
}
