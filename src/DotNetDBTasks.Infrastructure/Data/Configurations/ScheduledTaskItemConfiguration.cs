using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class ScheduledTaskItemConfiguration : IEntityTypeConfiguration<ScheduledTaskItem>
{
    public void Configure(EntityTypeBuilder<ScheduledTaskItem> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ParametersJson).HasColumnType("CLOB");
        builder.Property(e => e.FileNamePrefix).HasMaxLength(200);
        builder.Property(e => e.KeyColumn).HasMaxLength(128);
        builder.Property(e => e.KeyParameter).HasMaxLength(128);
        builder.Property(e => e.InitialKey).HasMaxLength(500);
        builder.Property(e => e.LastKeyValue).HasMaxLength(500);

        // No cascade from DynamicQuery: deleting a query used by a schedule must fail
        // loudly rather than silently hollowing out the scheduled task.
        builder.HasOne(e => e.DynamicQuery)
            .WithMany()
            .HasForeignKey(e => e.DynamicQueryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(e => e.ScheduledTaskId);
        builder.HasIndex(e => e.DynamicQueryId);
    }
}
