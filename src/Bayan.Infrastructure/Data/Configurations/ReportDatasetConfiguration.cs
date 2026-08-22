using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ReportDatasetConfiguration : IEntityTypeConfiguration<ReportDataset>
{
    public void Configure(EntityTypeBuilder<ReportDataset> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.DatasetKey).HasMaxLength(100).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.LeftColumn).HasMaxLength(128);
        builder.Property(e => e.RightColumn).HasMaxLength(128);
        builder.Property(e => e.ColumnSelectionJson).HasColumnType("CLOB");

        // The template addresses datasets by key, so two datasets sharing one within a
        // report would make {{RESULTS:key}} ambiguous. Enforced in the database, not just
        // in the validator.
        builder.HasIndex(e => new { e.ReportId, e.DatasetKey })
            .IsUnique()
            .HasDatabaseName("IX_ReportDatasets_ReportId_DatasetKey");

        // No cascade from DynamicQuery: deleting a query a report depends on must fail
        // loudly rather than silently hollowing out the report.
        builder.HasOne(e => e.DynamicQuery)
            .WithMany()
            .HasForeignKey(e => e.DynamicQueryId)
            .OnDelete(DeleteBehavior.NoAction);

        // Dataset-to-dataset references carry no FK constraint (see the entity), just indexes.
        builder.Property(e => e.LeftDatasetId).HasColumnType("RAW(16)");
        builder.Property(e => e.RightDatasetId).HasColumnType("RAW(16)");
        builder.Property(e => e.ParentDatasetId).HasColumnType("RAW(16)");
        builder.HasIndex(e => e.ParentDatasetId).HasDatabaseName("IX_ReportDatasets_ParentDatasetId");
        builder.HasIndex(e => e.DynamicQueryId).HasDatabaseName("IX_ReportDatasets_DynamicQueryId");
    }
}
