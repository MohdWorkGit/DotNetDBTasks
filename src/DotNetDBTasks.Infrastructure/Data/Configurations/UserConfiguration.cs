using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Username).HasMaxLength(100).IsRequired();
        builder.HasIndex(e => e.Username).IsUnique();
        // Optional, but still unique when supplied. Oracle allows any number of NULLs in a
        // unique index, so users without an address do not collide with each other.
        builder.Property(e => e.Email).HasMaxLength(256);
        builder.HasIndex(e => e.Email).IsUnique();
        builder.Property(e => e.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.LastName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.RefreshToken).HasMaxLength(512);
        builder.Property(e => e.Department).HasMaxLength(200);
        builder.HasIndex(e => e.Department);
    }
}
