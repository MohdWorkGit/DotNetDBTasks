using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        // Nullable, not NOT NULL DEFAULT '': Oracle stores an empty string as NULL.
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.TemplateDocx).HasColumnType("BLOB");
        builder.Property(e => e.TemplateFileName).HasMaxLength(255);
        // Comma-separated ExportFileFormat names; NULL means the report cannot be exported.
        // No default: a new report starts with export closed, like every other capability.
        builder.Property(e => e.AllowedExportFormats).HasMaxLength(100);
        builder.Property(e => e.TimeoutSeconds).HasDefaultValue(120);
        builder.Property(e => e.MaxDetailRows).HasDefaultValue(100);

        builder.HasMany(e => e.Datasets)
            .WithOne(d => d.Report)
            .HasForeignKey(d => d.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Parameters)
            .WithOne(p => p.Report)
            .HasForeignKey(p => p.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Runs)
            .WithOne(r => r.Report)
            .HasForeignKey(r => r.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        // Same behaviour as DynamicQuery.QueryGroup: deleting a group orphans its contents
        // rather than deleting them.
        builder.HasOne(e => e.QueryGroup)
            .WithMany()
            .HasForeignKey(e => e.QueryGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.Name);
        builder.HasIndex(e => e.QueryGroupId);
    }
}
