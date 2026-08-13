using Bayan.Domain.Interfaces;

namespace Bayan.Application.Common.Security;

/// <summary>
/// Resolves which user groups the current user belongs to.
///
/// <para>
/// Read from the database on each access check rather than carried in the JWT, which is what
/// the AD department claim used to do. A token lives for an hour, so a claim-based membership
/// would leave a revoked group granting access until it expired — the one moment where being
/// current matters most. Membership is a two-column lookup on an indexed key, which is cheap
/// beside the query it is gating.
/// </para>
/// </summary>
public static class UserGroupMembership
{
    /// <summary>The ids of every group this user is a member of. Empty when they are in none.</summary>
    public static async Task<HashSet<Guid>> GroupIdsAsync(
        IUnitOfWork unitOfWork,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var memberships = await unitOfWork.UserGroupMembers.FindAsync(
            m => m.UserId == userId, cancellationToken);

        return memberships.Select(m => m.UserGroupId).ToHashSet();
    }
}
