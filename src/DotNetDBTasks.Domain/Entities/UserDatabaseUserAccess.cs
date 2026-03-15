namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// Join entity controlling which application users are allowed to use which database users.
/// Only users with an entry here (or Admins) can execute queries through the associated DatabaseUser.
/// </summary>
public class UserDatabaseUserAccess
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid DatabaseUserId { get; set; }
    public DatabaseUser DatabaseUser { get; set; } = null!;
}
