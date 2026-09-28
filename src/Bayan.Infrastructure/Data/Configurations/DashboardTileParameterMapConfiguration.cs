using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class DashboardTileParameterMapConfiguration : IEntityTypeConfiguration<DashboardTileParameterMap>
{
    public void Configure(EntityTypeBuilder<DashboardTileParameterMap> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TargetParameterName).HasMaxLength(128).IsRequired();
        builder.Property(e => e.ConstantValue).HasMaxLength(2000);

        // Only the tile owns a cascade into this table. The filter side is a plain value: a
        // second path Dashboard -> DashboardFilter -> map is one Oracle rejects.
        builder.Property(e => e.DashboardFilterId).HasColumnType("RAW(16)");
        builder.HasIndex(e => e.DashboardFilterId);

        // One value per target parameter per tile.
        builder.HasIndex(e => new { e.DashboardTileId, e.TargetParameterName })
            .IsUnique()
            .HasDatabaseName("IX_DashboardTileMaps_Tile_Target");
    }
}
