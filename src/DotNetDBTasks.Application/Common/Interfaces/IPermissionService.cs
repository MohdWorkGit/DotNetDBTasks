namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Resolves what a set of roles is currently allowed to do.
///
/// <para>
/// Read from the database on each check rather than trusted from the token. Permissions are
/// edited at runtime, and a token lives for an hour: a claim-based answer would leave a
/// capability an administrator has just revoked working until it expired. The lookup is two
/// indexed columns, which is cheap beside whatever it is gating.
/// </para>
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Every permission held by any of these roles, unioned. Admin resolves to all of them
    /// without consulting the table.
    /// </summary>
    Task<IReadOnlySet<string>> GetForRolesAsync(
        IEnumerable<string> roleNames, CancellationToken cancellationToken = default);

    /// <summary>True when any of these roles holds <paramref name="permission"/>.</summary>
    Task<bool> HasAsync(
        IEnumerable<string> roleNames, string permission, CancellationToken cancellationToken = default);
}
