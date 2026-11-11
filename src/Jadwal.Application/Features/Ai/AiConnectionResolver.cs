using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Common.Security;
using Jadwal.Domain.Entities;
using Jadwal.Domain.Exceptions;
using Jadwal.Domain.Interfaces;

namespace Jadwal.Application.Features.Ai;

/// <summary>
/// The gate every AI feature that reads a connection's structure goes through. The AI must be on;
/// a named connection must be one the caller may use and must have "Allow AI" switched on; the
/// default connection — Jadwal's own database — must be allowed in System Settings.
/// </summary>
public sealed class AiConnectionResolver
{
    private readonly IAiChatClient _ai;
    private readonly ISystemSettingsService _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IQueryExecutor _queryExecutor;
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly IEncryptionService _encryption;
    private readonly IAppLocalizer _messages;

    public AiConnectionResolver(
        IAiChatClient ai, ISystemSettingsService settings, IUnitOfWork unitOfWork, ICurrentUserService currentUser,
        IQueryExecutor queryExecutor, IDatabaseConnectionFactory connectionFactory, IEncryptionService encryption,
        IAppLocalizer messages)
    {
        _ai = ai;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _queryExecutor = queryExecutor;
        _connectionFactory = connectionFactory;
        _encryption = encryption;
        _messages = messages;
    }

    /// <summary>Where to read, once every check has passed; throws with the reason otherwise.</summary>
    public async Task<SchemaTarget> ResolveAsync(Guid? databaseUserId, CancellationToken cancellationToken)
    {
        await AiAvailability.EnsureEnabledAsync(_ai, _settings, _messages, cancellationToken);

        if (databaseUserId is null)
        {
            if (!await DefaultConnectionAllowedAsync(cancellationToken))
                throw new DomainException(_messages[MessageKeys.AiDefaultConnectionNotAllowed]);
            return new SchemaTarget(null, null, _queryExecutor.DefaultServerType);
        }

        var dbUser = await QueryAccess.EnsureCanUseDatabaseUserAsync(
            databaseUserId.Value, _unitOfWork, _currentUser, cancellationToken);
        if (!dbUser.AllowAi)
            throw new DomainException(_messages[MessageKeys.AiConnectionNotAllowed, dbUser.Name]);

        return new SchemaTarget(
            dbUser.Id,
            _connectionFactory.BuildConnectionString(dbUser, _encryption.Decrypt(dbUser.EncryptedPassword)),
            dbUser.ServerType);
    }

    public Task<bool> DefaultConnectionAllowedAsync(CancellationToken cancellationToken) =>
        _settings.GetBoolAsync(SystemSettingKeys.AiDefaultConnection, SystemSettingKeys.AiDefaultConnectionDefault, cancellationToken);
}
