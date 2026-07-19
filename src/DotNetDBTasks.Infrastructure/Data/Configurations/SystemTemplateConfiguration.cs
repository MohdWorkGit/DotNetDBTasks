using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class SystemTemplateConfiguration : IEntityTypeConfiguration<SystemTemplate>
{
    public void Configure(EntityTypeBuilder<SystemTemplate> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Key).HasMaxLength(50).IsRequired();
        builder.HasIndex(e => e.Key).IsUnique();
        builder.Property(e => e.FileName).HasMaxLength(255).IsRequired();
        builder.Property(e => e.Content).HasColumnType("BLOB").IsRequired();
    }
}
