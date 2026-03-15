using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class UserDatabaseUserAccessConfiguration : IEntityTypeConfiguration<UserDatabaseUserAccess>
{
    public void Configure(EntityTypeBuilder<UserDatabaseUserAccess> builder)
    {
        builder.ToTable("UserDatabaseUserAccess");

        builder.HasKey(e => new { e.UserId, e.DatabaseUserId });

        builder.HasOne(e => e.User)
            .WithMany(u => u.DatabaseUserAccess)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.DatabaseUser)
            .WithMany(d => d.UserAccess)
            .HasForeignKey(e => e.DatabaseUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
