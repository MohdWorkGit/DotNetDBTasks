namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Tracks in-flight scheduled-task runs so an admin can stop one on demand. The runner registers a
/// run when it starts executing and unregisters it when it finishes; cancelling signals the run's
/// linked token, which flows through the query pipeline and aborts the executing database command —
/// the same real cancellation path an interactive job uses. In-memory per API instance (like the
/// run queue), so it is authoritative only for runs executing on this instance.
/// </summary>
public interface IScheduledTaskRunRegistry
{
    /// <summary>
    /// Registers a starting run and returns a <see cref="CancellationTokenSource"/> linked to
    /// <paramref name="linkedToken"/> (typically the worker's shutdown token). The runner executes
    /// under the returned token and MUST call <see cref="Unregister"/> when the run completes.
    /// </summary>
    CancellationTokenSource Register(Guid runId, CancellationToken linkedToken);

    /// <summary>Removes and disposes a finished run's registration. No-op if it is not registered.</summary>
    void Unregister(Guid runId);

    /// <summary>
    /// Signals cancellation for a running run. Returns false if no such run is currently in flight
    /// on this instance (already finished, never started, or running elsewhere).
    /// </summary>
    bool Cancel(Guid runId);
}
