using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Features.DatabaseUsers.Queries;
using Jadwal.Domain.Entities;
using Jadwal.Domain.Enums;
using Jadwal.Domain.Interfaces;
using MediatR;

namespace Jadwal.Application.Features.DatabaseUsers.Commands;

public class CreateDatabaseUserCommand : IRequest<DatabaseUserDto>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DatabaseServerType ServerType { get; set; } = DatabaseServerType.Oracle;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1521;
    public string ServiceName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string DbUsername { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool AllowAi { get; set; }
}

public class CreateDatabaseUserCommandHandler
    : IRequestHandler<CreateDatabaseUserCommand, DatabaseUserDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEncryptionService _encryption;

    public CreateDatabaseUserCommandHandler(
        IUnitOfWork unitOfWork,
        IEncryptionService encryption)
    {
        _unitOfWork = unitOfWork;
        _encryption = encryption;
    }

    public async Task<DatabaseUserDto> Handle(
        CreateDatabaseUserCommand request,
        CancellationToken cancellationToken)
    {
        var entity = new DatabaseUser
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            ServerType = request.ServerType,
            Host = request.Host,
            Port = request.Port,
            // Store null instead of empty string — Oracle treats "" as NULL which
            // violates NOT NULL constraints. Only populate the field relevant to the ServerType.
            ServiceName = string.IsNullOrEmpty(request.ServiceName) ? null : request.ServiceName,
            DatabaseName = string.IsNullOrEmpty(request.DatabaseName) ? null : request.DatabaseName,
            DbUsername = request.DbUsername,
            EncryptedPassword = _encryption.Encrypt(request.Password),
            IsActive = true,
            AllowAi = request.AllowAi,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.DatabaseUsers.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DatabaseUserDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            ServerType = entity.ServerType,
            Host = entity.Host,
            Port = entity.Port,
            ServiceName = entity.ServiceName ?? string.Empty,
            DatabaseName = entity.DatabaseName ?? string.Empty,
            DbUsername = entity.DbUsername,
            IsActive = entity.IsActive,
            AllowAi = entity.AllowAi,
            CreatedAt = entity.CreatedAt
        };
    }
}
