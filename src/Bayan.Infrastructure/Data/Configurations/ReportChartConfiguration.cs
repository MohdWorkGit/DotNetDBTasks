using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ReportChartConfiguration : IEntityTypeConfiguration<ReportChart>
{
    public void Configure(EntityTypeBuilder<ReportChart> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ChartKey).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.CategoryColumn).HasMaxLength(128).IsRequired();
        builder.Property(e => e.SeriesColumnsJson).HasColumnType("CLOB").IsRequired();
        builder.Property(e => e.MaxCategories).HasDefaultValue(25);

        builder.HasOne(e => e.Report)
            .WithMany(r => r.Charts)
            .HasForeignKey(e => e.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        // The template addresses charts by key, so a duplicate within a report would make its
        // {{CHART:key}} marker ambiguous.
        builder.HasIndex(e => new { e.ReportId, e.ChartKey })
            .IsUnique()
            .HasDatabaseName("IX_ReportCharts_ReportId_ChartKey");

        // Plain indexed value, no constraint: the dataset it draws from lives in the same
        // report and is validated on save, the same shape as the dataset-to-dataset links.
        builder.Property(e => e.DatasetId).HasColumnType("RAW(16)");
        builder.HasIndex(e => e.DatasetId);
    }
}
