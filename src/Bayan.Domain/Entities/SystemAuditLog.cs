namespace Bayan.Domain.Entities;

/// <summary>
/// One administrative action — a user created, a permission granted, a query edited.
///
/// <para>
/// Distinct from <see cref="QueryExecutionLog"/>, which records *runs* of a query. This table
/// records changes to the system's configuration: who changed what, and when. The two are kept
/// apart because they answer different questions and have very different volumes.
/// </para>
///
/// <para>
/// Entries are written by the MediatR audit behavior, so every command is covered without each
/// handler having to remember. Deliberately append-only: nothing in the application updates or
/// deletes a row here, which is the whole point of an audit trail.
/// </para>
/// </summary>
public class SystemAuditLog : BaseEntity
{
    public DateTime OccurredAt { get; set; }

    /// <summary>Null for actions taken by a background worker rather than a signed-in person.</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Denormalized on purpose. The audit trail has to stay readable after the account is
    /// renamed or removed, so it cannot depend on a join to <c>Users</c>.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Stable machine code such as <c>users.create</c> or <c>queries.update</c> — never a
    /// sentence. The client turns it into text, which is what lets the log read in Arabic as
    /// well as English. Unrecognised codes render as-is rather than disappearing.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Coarse grouping for the filter dropdown: <c>users</c>, <c>queries</c>, …</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Identifier of the thing acted on, when the action targets one row.</summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// Human-readable name of the target at the time of the action — a username, a query name.
    /// Also denormalized, for the same reason as <see cref="Username"/>.
    /// </summary>
    public string? EntityName { get; set; }

    /// <summary>
    /// JSON snapshot of what was submitted, with secrets stripped before it ever reaches here.
    /// Answers "what did they change it to" without a second table of before/after values.
    /// </summary>
    public string? DetailsJson { get; set; }

    /// <summary>
    /// False when the action was attempted and rejected. Failures are recorded too: a denied
    /// permission change is often more interesting than a successful one.
    /// </summary>
    public bool IsSuccess { get; set; } = true;

    public string? ErrorMessage { get; set; }

    /// <summary>Caller's IP, as seen after ForwardedHeaders has resolved any proxy.</summary>
    public string? IpAddress { get; set; }
}
