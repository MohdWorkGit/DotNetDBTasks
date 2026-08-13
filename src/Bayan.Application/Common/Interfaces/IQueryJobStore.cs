using Bayan.Application.Common.Models;
using Bayan.Application.Features.QueryExecution.Commands;

namespace Bayan.Application.Common.Interfaces;

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

    /// <summary>
    /// Execution metadata (columns, totals, preview, parameters). For a read result the potentially
    /// large row set is NOT kept here — it lives in <see cref="CachedRows"/> — so this object stays
    /// small in the heap.
    /// </summary>
    public QueryExecutionResult? Result { get; set; }

    /// <summary>
    /// The cached read rows, held either in the heap (small) or spilled to disk (large). Null for
    /// write/preview results. Paging and export go through this; it is disposed on eviction/removal.
    /// </summary>
    public ICachedResult? CachedRows { get; set; }

    public string? Error { get; set; }
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Last time the job was read (polled, paged, or exported). Retention is sliding: a job is
    /// evicted only after it has been idle — untouched — for the retention window, so a page that
    /// is actively viewing/paging its result keeps it alive.
    /// </summary>
    public DateTime LastAccessedAt { get; set; }

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
    /// Records a successful execution result on the job and marks it Succeeded. For a read result
    /// the rows are moved into <see cref="QueryJob.CachedRows"/> (spilled to disk when large) and
    /// cleared from <see cref="QueryExecutionResult.Rows"/> so only metadata stays in the heap.
    /// </summary>
    void SetResult(Guid id, QueryExecutionResult result);

    /// <summary>
    /// Drops a job and its cached result immediately (used when the client leaves the results
    /// page). Disposes any spilled disk artifacts. No-op if the job does not exist.
    /// </summary>
    void Remove(Guid id);

    /// <summary>
    /// Signals cancellation for a running job and marks it Canceled. Returns false if
    /// the job does not exist or has already reached a terminal state.
    /// </summary>
    bool Cancel(Guid id);

    /// <summary>
    /// Evicts results idle beyond the retention window and enforces the heap/disk size budgets via
    /// LRU eviction (never evicting queued/running jobs), disposing artifacts as it goes. Invoked on
    /// a timer by the maintenance background service.
    /// </summary>
    void RunMaintenance();

    /// <summary>How often <see cref="RunMaintenance"/> should be invoked.</summary>
    TimeSpan MaintenanceInterval { get; }
}
