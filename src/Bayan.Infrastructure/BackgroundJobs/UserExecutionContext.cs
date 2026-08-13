using Bayan.Application.Common.Interfaces;

namespace Bayan.Infrastructure.BackgroundJobs;

/// <summary>
/// AsyncLocal-backed implementation of <see cref="IUserExecutionContext"/>. Registered
/// as a singleton; the AsyncLocal ensures each concurrent job's async flow sees its own
/// snapshot even though the holder instance is shared.
/// </summary>
public class UserExecutionContext : IUserExecutionContext
{
    private static readonly AsyncLocal<UserContextSnapshot?> _current = new();

    public UserContextSnapshot? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
