using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class DashboardTileConfiguration : IEntityTypeConfiguration<DashboardTile>
{
    public void Configure(EntityTypeBuilder<DashboardTile> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Width).HasDefaultValue(4);
        builder.Property(e => e.Height).HasDefaultValue(1);
        builder.Property(e => e.CategoryColumn).HasMaxLength(128);
        builder.Property(e => e.SeriesColumnsJson).HasColumnType("CLOB").IsRequired();
        builder.Property(e => e.MaxCategories).HasDefaultValue(25);
        builder.Property(e => e.ValueColumn).HasMaxLength(128);
        builder.Property(e => e.CompareColumn).HasMaxLength(128);
        builder.Property(e => e.DrillReportParameter).HasMaxLength(100);
        builder.Property(e => e.TargetValue).HasPrecision(18, 4);
        builder.Property(e => e.TargetWarnPercent).HasDefaultValue(10);
        builder.Property(e => e.ConditionalRulesJson).HasColumnType("CLOB");

        // No cascade from DynamicQuery: deleting a query a tile depends on must fail loudly
        // rather than silently emptying the dashboard — the rule report datasets follow.
        builder.HasOne(e => e.DynamicQuery)
            .WithMany()
            .HasForeignKey(e => e.DynamicQueryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(e => e.ParameterMaps)
            .WithOne(m => m.DashboardTile)
            .HasForeignKey(m => m.DashboardTileId)
            .OnDelete(DeleteBehavior.Cascade);

        // Plain indexed values, no constraint: resolved at run time (see the entity).
        builder.Property(e => e.DrillFilterId).HasColumnType("RAW(16)");
        builder.Property(e => e.DrillReportId).HasColumnType("RAW(16)");

        builder.HasIndex(e => e.DashboardId);
        builder.HasIndex(e => e.DynamicQueryId).HasDatabaseName("IX_DashboardTiles_DynamicQueryId");
    }
}
