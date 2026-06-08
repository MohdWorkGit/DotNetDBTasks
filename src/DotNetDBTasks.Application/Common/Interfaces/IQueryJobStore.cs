using DotNetDBTasks.Application.Common.Models;
using DotNetDBTasks.Application.Features.QueryExecution.Commands;

namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Lifecycle state of an async query execution job.
/// </summary>
public enum QueryJobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Canceled
}

/// <summary>
/// An in-flight (or recently completed) async query execution. Created when a user
/// submits a query, processed by the background worker, and polled by the client until
/// it reaches a terminal state.
/// </summary>
public class QueryJob
{
    public Guid Id { get; init; }

    /// <summary>The user who submitted the job; used to authorize polling/cancel.</summary>
    public Guid UserId { get; init; }

    /// <summary>Identity snapshot the worker uses to reconstruct the user context.</summary>
    public UserContextSnapshot UserSnapshot { get; init; } = null!;

    /// <summary>The command the worker replays via MediatR.</summary>
    public ExecuteQueryCommand Command { get; init; } = null!;

    public QueryJobStatus Status { get; set; } = QueryJobStatus.Queued;
    public QueryExecutionResult? Result { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Cancellation source for this job, triggered by the cancel endpoint. Never
    /// serialized — it lives only in the in-memory store.
    /// </summary>
    public CancellationTokenSource Cts { get; init; } = new();
}

/// <summary>
/// In-memory registry of async query jobs. Implementations are expected to be a
/// singleton and to evict completed jobs after a retention window.
/// </summary>
public interface IQueryJobStore
{
    QueryJob Create(Guid userId, UserContextSnapshot snapshot, ExecuteQueryCommand command);
    QueryJob? Get(Guid id);
    void Update(Guid id, Action<QueryJob> mutate);

    /// <summary>
    /// Signals cancellation for a running job and marks it Canceled. Returns false if
    /// the job does not exist or has already reached a terminal state.
    /// </summary>
    bool Cancel(Guid id);
}
