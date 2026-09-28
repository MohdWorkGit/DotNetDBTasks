using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

/// <summary>Mirrors <see cref="ReportParameterConfiguration"/> field for field.</summary>
public class DashboardFilterConfiguration : IEntityTypeConfiguration<DashboardFilter>
{
    public void Configure(EntityTypeBuilder<DashboardFilter> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DefaultValue).HasMaxLength(500);

        builder.Property(e => e.DropdownStaticValues).HasColumnType("CLOB");
        builder.Property(e => e.DropdownQueryValueColumn).HasMaxLength(100);
        builder.Property(e => e.DropdownQueryLabelColumn).HasMaxLength(100);

        builder.Property(e => e.DropdownQueryId).HasColumnType("RAW(16)");
        builder.HasIndex(e => e.DropdownQueryId).HasDatabaseName("IX_DashboardFilters_DropdownQueryId");

        // The filter name is the key in the dashboard URL and in the tile maps.
        builder.HasIndex(e => new { e.DashboardId, e.Name })
            .IsUnique()
            .HasDatabaseName("IX_DashboardFilters_DashboardId_Name");
    }
}
