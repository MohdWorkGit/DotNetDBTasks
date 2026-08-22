using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ReportParameterMapConfiguration : IEntityTypeConfiguration<ReportParameterMap>
{
    public void Configure(EntityTypeBuilder<ReportParameterMap> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TargetParameterName).HasMaxLength(128).IsRequired();
        builder.Property(e => e.ConstantValue).HasMaxLength(2000);
        builder.Property(e => e.ParentColumn).HasMaxLength(128);

        // Only ONE cascade path may reach this table. Report -> ReportDataset -> map and
        // Report -> ReportParameter -> map would be two, which Oracle rejects, so the
        // dataset owns the cascade and the parameter side is NoAction. Removing a report
        // parameter therefore clears its maps in the application layer, which the builder
        // does anyway when it rewrites the report's graph on save.
        builder.HasOne(e => e.ReportDataset)
            .WithMany(d => d.ParameterMaps)
            .HasForeignKey(e => e.ReportDatasetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.ReportParameter)
            .WithMany(p => p.Maps)
            .HasForeignKey(e => e.ReportParameterId)
            .OnDelete(DeleteBehavior.NoAction);

        // One value per target parameter per dataset — two maps feeding the same @name would
        // make the bound value depend on load order.
        builder.HasIndex(e => new { e.ReportDatasetId, e.TargetParameterName })
            .IsUnique()
            .HasDatabaseName("IX_ReportParameterMaps_Dataset_Target");
        builder.HasIndex(e => e.ReportParameterId);
    }
}
