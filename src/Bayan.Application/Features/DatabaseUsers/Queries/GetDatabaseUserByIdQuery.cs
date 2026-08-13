using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DatabaseUsers.Queries;

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
        var du = await _unitOfWork.DatabaseUsers.GetByIdAsync(request.Id, cancellationToken, "RoleAccess");
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

        return dto;
    }
}
