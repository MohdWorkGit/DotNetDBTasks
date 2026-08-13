using System.Security.Claims;
using DotNetDBTasks.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace DotNetDBTasks.API.Authorization;

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
/// </summary>
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public RequirePermissionAttribute(string permission)
        : base(PolicyPrefix + permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}

/// <summary>The permission a policy demands, carried from its name to the handler.</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission) => Permission = permission;

    public string Permission { get; }
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

        var permission = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
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

        if (await _permissions.HasAsync(roles, requirement.Permission))
            context.Succeed(requirement);
    }
}
