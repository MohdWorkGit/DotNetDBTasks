using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Features.DatabaseUsers.Queries;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DatabaseUsers.Commands;

public class CreateDatabaseUserCommand : IRequest<DatabaseUserDto>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1521;
    public string ServiceName { get; set; } = string.Empty;
    public string DbUsername { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
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
            Host = request.Host,
            Port = request.Port,
            ServiceName = request.ServiceName,
            DbUsername = request.DbUsername,
            EncryptedPassword = _encryption.Encrypt(request.Password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.DatabaseUsers.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DatabaseUserDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Host = entity.Host,
            Port = entity.Port,
            ServiceName = entity.ServiceName,
            DbUsername = entity.DbUsername,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt
        };
    }
}
