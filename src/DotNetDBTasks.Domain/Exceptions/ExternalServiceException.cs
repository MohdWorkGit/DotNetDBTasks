namespace DotNetDBTasks.Domain.Exceptions;

/// <summary>
/// A dependency outside this application failed — the directory server, most often.
///
/// <para>
/// Distinct from <see cref="DomainException"/> because nothing the caller submitted is
/// wrong: the request was fine and the far side did not answer. The middleware maps it to
/// 502 so the client can say "AD is unreachable" rather than blaming the user's input.
/// </para>
/// </summary>
public class ExternalServiceException : Exception
{
    public ExternalServiceException(string message) : base(message) { }
    public ExternalServiceException(string message, Exception innerException)
        : base(message, innerException) { }
}
