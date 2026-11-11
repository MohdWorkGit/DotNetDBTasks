using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Common.Security;
using Jadwal.Domain.Enums;
using Jadwal.Domain.Interfaces;

namespace Jadwal.Application.Features.Ai.Commands;

internal static class AiSqlCommandSupport
{
    /// <summary>
    /// The kind of database the SQL is written for, so the model knows the dialect: the chosen
    /// connection's, under the same may-you-use-it check as a test run, or the default one's.
    /// Only the type is used — the model is never given the connection or any of its data.
    /// </summary>
    public static async Task<DatabaseServerType> ResolveServerTypeAsync(
        Guid? databaseUserId, IUnitOfWork unitOfWork, ICurrentUserService currentUser,
        IQueryExecutor queryExecutor, CancellationToken cancellationToken)
    {
        if (!databaseUserId.HasValue)
            return queryExecutor.DefaultServerType;

        var dbUser = await QueryAccess.EnsureCanUseDatabaseUserAsync(
            databaseUserId.Value, unitOfWork, currentUser, cancellationToken);
        return dbUser.ServerType;
    }
}
