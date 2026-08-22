using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ReportRunConfiguration : IEntityTypeConfiguration<ReportRun>
{
    public void Configure(EntityTypeBuilder<ReportRun> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ParametersJson).HasColumnType("CLOB");
        builder.Property(e => e.DatasetResultsJson).HasColumnType("CLOB");
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(e => e.ReportId);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.StartedAt);
    }
}
