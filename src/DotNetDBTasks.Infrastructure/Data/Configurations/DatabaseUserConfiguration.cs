using DotNetDBTasks.Domain.Entities;
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

        builder.Property(e => e.Host)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Port)
            .HasDefaultValue(1521);

        builder.Property(e => e.ServiceName)
            .IsRequired()
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
