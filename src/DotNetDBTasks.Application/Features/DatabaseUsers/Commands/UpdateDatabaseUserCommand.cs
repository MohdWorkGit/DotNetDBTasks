using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Features.DatabaseUsers.Queries;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DatabaseUsers.Commands;

public class UpdateDatabaseUserCommand : IRequest<DatabaseUserDto>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DatabaseServerType ServerType { get; set; } = DatabaseServerType.Oracle;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1521;
    public string ServiceName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string DbUsername { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>
    /// If provided, the password is re-encrypted and updated. If null/empty, the existing password is preserved.
    /// </summary>
    public string? Password { get; set; }
}

public class UpdateDatabaseUserCommandHandler
    : IRequestHandler<UpdateDatabaseUserCommand, DatabaseUserDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEncryptionService _encryption;

    public UpdateDatabaseUserCommandHandler(
        IUnitOfWork unitOfWork,
        IEncryptionService encryption)
    {
        _unitOfWork = unitOfWork;
        _encryption = encryption;
    }

    public async Task<DatabaseUserDto> Handle(
        UpdateDatabaseUserCommand request,
        CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.DatabaseUsers.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
            throw new NotFoundException(nameof(DatabaseUser), request.Id);

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.ServerType = request.ServerType;
        entity.Host = request.Host;
        entity.Port = request.Port;
        // Store null instead of empty string — Oracle treats "" as NULL.
        entity.ServiceName = string.IsNullOrEmpty(request.ServiceName) ? null : request.ServiceName;
        entity.DatabaseName = string.IsNullOrEmpty(request.DatabaseName) ? null : request.DatabaseName;
        entity.DbUsername = request.DbUsername;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            entity.EncryptedPassword = _encryption.Encrypt(request.Password);
        }

        _unitOfWork.DatabaseUsers.Update(entity);
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
            CreatedAt = entity.CreatedAt
        };
    }
}
