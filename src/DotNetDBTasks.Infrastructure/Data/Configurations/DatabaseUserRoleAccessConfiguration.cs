using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class DatabaseUserRoleAccessConfiguration : IEntityTypeConfiguration<DatabaseUserRoleAccess>
{
    public void Configure(EntityTypeBuilder<DatabaseUserRoleAccess> builder)
    {
        builder.ToTable("DatabaseUserRoleAccess");

        builder.HasKey(e => new { e.RoleId, e.DatabaseUserId });

        builder.HasOne(e => e.Role)
            .WithMany(r => r.DatabaseUserAccess)
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.DatabaseUser)
            .WithMany(d => d.RoleAccess)
            .HasForeignKey(e => e.DatabaseUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
