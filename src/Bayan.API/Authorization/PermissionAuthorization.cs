using System.Security.Claims;
using Bayan.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace Bayan.API.Authorization;

/// <summary>
/// Requires a capability rather than a role name.
///
/// <para>
/// Endpoints used to name roles — <c>[Authorize(Roles = "Admin,AccessManager")]</c> — which
/// meant the answer to "who may do this?" was compiled in, and a role an installation invented
/// could never be part of it. Naming the capability instead leaves the answer where an
/// administrator can edit it, on Settings → Permissions.
/// </para>
///
/// <para>Still authentication-gated: no permission is granted to an anonymous caller.</para>
///
/// <para>Naming more than one permission means <b>any of them</b> will do, not all. Two
/// attributes stacked on the same action already mean "and"; this covers the other case — a
/// page that two different capabilities can both reach, such as the query list that now also
/// carries reports. Keep the list short: a long any-of is usually a sign the endpoint is doing
/// two jobs.</para>
/// </summary>
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    /// <summary>Separator in the policy name for an any-of list. Not legal in a permission name.</summary>
    public const char AnyOfSeparator = '|';

    public RequirePermissionAttribute(params string[] permissions)
        : base(PolicyPrefix + string.Join(AnyOfSeparator, permissions))
    {
        Permissions = permissions;
    }

    public IReadOnlyList<string> Permissions { get; }
}

/// <summary>
/// The permissions a policy accepts, carried from its name to the handler. Holding any one of
/// them satisfies the requirement.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(IReadOnlyList<string> permissions) => Permissions = permissions;

    public IReadOnlyList<string> Permissions { get; }
}

/// <summary>
/// Builds a policy on demand for any <c>perm:&lt;name&gt;</c> asked for.
///
/// <para>Policies are normally registered up front by name. There are two dozen permissions and
/// the set grows, so registering them one by one is a list to forget to update; this mints the
/// policy from the name instead. Anything not prefixed falls through to the default provider,
/// which still handles plain <c>[Authorize]</c>.</para>
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return _fallback.GetPolicyAsync(policyName);

        var permissions = policyName[RequirePermissionAttribute.PolicyPrefix.Length..]
            .Split(RequirePermissionAttribute.AnyOfSeparator, StringSplitOptions.RemoveEmptyEntries);
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissions))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

/// <summary>
/// Answers the requirement by asking what the caller's roles currently hold.
///
/// <para>The roles come from the token; the permissions they carry do not. That split is the
/// point: revoking a capability takes effect on the next request, without waiting for anyone's
/// token to expire.</para>
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissions;

    public PermissionAuthorizationHandler(IPermissionService permissions)
    {
        _permissions = permissions;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        if (roles.Count == 0)
            return;

        foreach (var permission in requirement.Permissions)
        {
            if (await _permissions.HasAsync(roles, permission))
            {
                context.Succeed(requirement);
                return;
            }
        }
    }
}
