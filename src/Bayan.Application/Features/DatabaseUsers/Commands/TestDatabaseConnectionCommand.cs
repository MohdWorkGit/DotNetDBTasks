using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DatabaseUsers.Commands;

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
    private readonly IDatabaseConnectionFactory _connectionFactory;

    public TestDatabaseConnectionCommandHandler(
        IUnitOfWork unitOfWork,
        IEncryptionService encryption,
        IQueryExecutor queryExecutor,
        IDatabaseConnectionFactory connectionFactory)
    {
        _unitOfWork = unitOfWork;
        _encryption = encryption;
        _queryExecutor = queryExecutor;
        _connectionFactory = connectionFactory;
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
            var connectionString = _connectionFactory.BuildConnectionString(dbUser, password);
            var testQuery = _connectionFactory.GetTestQuery(dbUser);

            await _queryExecutor.ExecuteAsync(
                testQuery,
                new Dictionary<string, object?>(),
                10,
                connectionString,
                dbUser.ServerType,
                cancellationToken);

            return new TestConnectionResult { Success = true };
        }
        catch (Exception ex)
        {
            return new TestConnectionResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}
