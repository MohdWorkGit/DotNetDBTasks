using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class DashboardConfiguration : IEntityTypeConfiguration<Dashboard>
{
    public void Configure(EntityTypeBuilder<Dashboard> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        // Nullable, not NOT NULL DEFAULT '': Oracle stores an empty string as NULL.
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.DefaultRefreshSeconds).HasDefaultValue(60);

        builder.HasMany(e => e.Tiles)
            .WithOne(t => t.Dashboard)
            .HasForeignKey(t => t.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Filters)
            .WithOne(f => f.Dashboard)
            .HasForeignKey(f => f.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.Name);
    }
}
