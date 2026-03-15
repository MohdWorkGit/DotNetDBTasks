using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DatabaseUsers.Commands;

/// <summary>
/// Tests the connection to the database using the stored (encrypted) credentials.
/// </summary>
public class TestDatabaseConnectionCommand : IRequest<TestConnectionResult>
{
    public Guid DatabaseUserId { get; set; }
}

public class TestConnectionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class TestDatabaseConnectionCommandHandler
    : IRequestHandler<TestDatabaseConnectionCommand, TestConnectionResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEncryptionService _encryption;
    private readonly IQueryExecutor _queryExecutor;

    public TestDatabaseConnectionCommandHandler(
        IUnitOfWork unitOfWork,
        IEncryptionService encryption,
        IQueryExecutor queryExecutor)
    {
        _unitOfWork = unitOfWork;
        _encryption = encryption;
        _queryExecutor = queryExecutor;
    }

    public async Task<TestConnectionResult> Handle(
        TestDatabaseConnectionCommand request,
        CancellationToken cancellationToken)
    {
        var dbUser = await _unitOfWork.DatabaseUsers.GetByIdAsync(request.DatabaseUserId, cancellationToken);
        if (dbUser is null)
            throw new NotFoundException(nameof(DatabaseUser), request.DatabaseUserId);

        try
        {
            var password = _encryption.Decrypt(dbUser.EncryptedPassword);
            var connectionString = BuildConnectionString(dbUser, password);

            await _queryExecutor.ExecuteAsync(
                "SELECT 1 FROM DUAL",
                new Dictionary<string, object?>(),
                10,
                connectionString,
                cancellationToken);

            return new TestConnectionResult { Success = true };
        }
        catch (Exception ex)
        {
            return new TestConnectionResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static string BuildConnectionString(DatabaseUser dbUser, string password)
    {
        return $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={dbUser.Host})(PORT={dbUser.Port}))(CONNECT_DATA=(SERVICE_NAME={dbUser.ServiceName})));User Id={dbUser.DbUsername};Password={password};";
    }
}
