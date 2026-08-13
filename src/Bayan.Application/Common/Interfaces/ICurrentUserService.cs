namespace Bayan.Application.Common.Interfaces;

/// <summary>
/// Provides access to the current authenticated user's context.
/// </summary>
public interface ICurrentUserService
{
    Guid UserId { get; }
    string Username { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsAuthenticated { get; }
}
