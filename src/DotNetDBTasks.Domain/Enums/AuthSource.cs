namespace DotNetDBTasks.Domain.Enums;

/// <summary>
/// Identifies whether a user authenticates via the local database or LDAP/Active Directory.
/// </summary>
public enum AuthSource
{
    Local = 0,
    Ldap = 1
}
