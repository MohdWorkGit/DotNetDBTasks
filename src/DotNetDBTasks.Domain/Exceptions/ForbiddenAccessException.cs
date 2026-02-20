namespace DotNetDBTasks.Domain.Exceptions;

/// <summary>
/// Thrown when a user attempts an action they are not authorized to perform.
/// </summary>
public class ForbiddenAccessException : DomainException
{
    public ForbiddenAccessException() : base("Access to this resource is forbidden.") { }
    public ForbiddenAccessException(string message) : base(message) { }
}
