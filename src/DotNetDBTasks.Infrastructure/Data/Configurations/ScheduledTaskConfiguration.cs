using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class ScheduledTaskConfiguration : IEntityTypeConfiguration<ScheduledTask>
{
    public void Configure(EntityTypeBuilder<ScheduledTask> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.OutputFolder).HasMaxLength(500).IsRequired();
        builder.Property(e => e.TimeOfDay).HasMaxLength(5);

        builder.HasIndex(e => e.Name).IsUnique();
        builder.HasIndex(e => e.NextRunAt);

        builder.HasMany(e => e.Items)
            .WithOne(i => i.ScheduledTask)
            .HasForeignKey(i => i.ScheduledTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Runs)
            .WithOne(r => r.ScheduledTask)
            .HasForeignKey(r => r.ScheduledTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Viewers)
            .WithOne(v => v.ScheduledTask)
            .HasForeignKey(v => v.ScheduledTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
