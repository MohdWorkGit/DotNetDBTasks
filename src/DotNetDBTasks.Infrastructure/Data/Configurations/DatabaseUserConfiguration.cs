using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class DatabaseUserConfiguration : IEntityTypeConfiguration<DatabaseUser>
{
    public void Configure(EntityTypeBuilder<DatabaseUser> builder)
    {
        builder.ToTable("DatabaseUsers");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.ServerType)
            .HasDefaultValue(DatabaseServerType.Oracle);

        builder.Property(e => e.Host)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Port)
            .HasDefaultValue(1521);

        // ServiceName is only used for Oracle connections; nullable to avoid
        // ORA-01400 when inserting non-Oracle database users (Oracle treats "" as NULL).
        builder.Property(e => e.ServiceName)
            .IsRequired(false)
            .HasMaxLength(200);

        // DatabaseName is only used for non-Oracle connections; nullable for the same reason.
        builder.Property(e => e.DatabaseName)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.Property(e => e.DbUsername)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.EncryptedPassword)
            .IsRequired()
            .HasMaxLength(1000);

        builder.HasIndex(e => e.Name).IsUnique();
    }
}
