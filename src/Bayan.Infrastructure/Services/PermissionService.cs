using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Constants;
using Bayan.Domain.Interfaces;

namespace Bayan.Infrastructure.Services;

/// <summary>
/// Reads the role → permission matrix out of <c>RolePermissions</c>.
///
/// <para>
/// Not cached, for the reason on <see cref="IPermissionService"/>: this is an authorization
/// path, and a stale answer here is a capability someone believes they revoked.
/// </para>
/// </summary>
public class PermissionService : IPermissionService
{
    private readonly IUnitOfWork _unitOfWork;

    public PermissionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlySet<string>> GetForRolesAsync(
        IEnumerable<string> roleNames, CancellationToken cancellationToken = default)
    {
        var names = roleNames as IReadOnlyCollection<string> ?? roleNames.ToList();

        // Admin is pinned rather than stored, so it cannot be edited into a corner — and so
        // an empty matrix (a database mid-upgrade, a botched save) still leaves someone able
        // to open the Permissions tab and fix it.
        if (names.Any(Permissions.IsPinned))
            return Permissions.All.ToHashSet();

        if (names.Count == 0)
            return new HashSet<string>();

        var roles = await _unitOfWork.Roles.FindAsync(
            r => names.Contains(r.Name), cancellationToken, "RolePermissions");

        return roles
            .SelectMany(r => r.RolePermissions.Select(p => p.Permission))
            .ToHashSet();
    }

    public async Task<bool> HasAsync(
        IEnumerable<string> roleNames, string permission, CancellationToken cancellationToken = default)
    {
        var held = await GetForRolesAsync(roleNames, cancellationToken);
        return held.Contains(permission);
    }
}
