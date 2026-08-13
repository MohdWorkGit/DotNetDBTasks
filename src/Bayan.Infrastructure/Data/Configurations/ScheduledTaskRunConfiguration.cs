using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ScheduledTaskRunConfiguration : IEntityTypeConfiguration<ScheduledTaskRun>
{
    public void Configure(EntityTypeBuilder<ScheduledTaskRun> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TriggeredByUsername).HasMaxLength(200);
        builder.Property(e => e.Error).HasMaxLength(2000);
        builder.Property(e => e.ItemResultsJson).HasColumnType("CLOB");

        builder.HasIndex(e => e.ScheduledTaskId);
        builder.HasIndex(e => e.StartedAt);
    }
}
