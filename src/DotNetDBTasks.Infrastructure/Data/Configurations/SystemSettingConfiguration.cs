using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Key).HasMaxLength(100).IsRequired();
        // Unique: a duplicate key would make which value wins depend on row order.
        builder.HasIndex(e => e.Key).IsUnique();
        builder.Property(e => e.Value).HasMaxLength(400).IsRequired();
    }
}
