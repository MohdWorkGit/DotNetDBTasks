namespace Bayan.Domain.Exceptions;

/// <summary>
/// Thrown when a query is aborted because its configured timeout elapsed. Database
/// drivers report an expired CommandTimeout as a generic cancellation ("a task was
/// canceled", ORA-01013, ...); this exception exists so users see an explicit
/// timeout message instead.
/// </summary>
public class QueryTimeoutException : DomainException
{
    public int TimeoutSeconds { get; }

    public QueryTimeoutException(int timeoutSeconds, Exception innerException)
        : base($"Query timed out: execution exceeded the configured timeout of {timeoutSeconds} second(s).",
               innerException)
    {
        TimeoutSeconds = timeoutSeconds;
    }
}
