namespace Bayan.Application.Common.Interfaces;

/// <summary>
/// Immutable snapshot of the authenticated user captured at request time, so the
/// background query worker can reconstruct the user's identity off the HTTP thread.
/// </summary>
public record UserContextSnapshot(
    Guid UserId,
    string Username,
    IReadOnlyList<string> Roles);

/// <summary>
/// Ambient holder for the current user snapshot, used by background work that runs
/// outside an HTTP request. Backed by an AsyncLocal so each concurrent job sees its
/// own value. <see cref="ICurrentUserService"/> falls back to this when there is no
/// authenticated HTTP context.
/// </summary>
public interface IUserExecutionContext
{
    UserContextSnapshot? Current { get; set; }
}
