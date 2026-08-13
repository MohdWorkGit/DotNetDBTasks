using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ScheduledTaskTriggerConfiguration : IEntityTypeConfiguration<ScheduledTaskTrigger>
{
    public void Configure(EntityTypeBuilder<ScheduledTaskTrigger> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TimeOfDay).HasMaxLength(5);

        builder.HasIndex(e => e.ScheduledTaskId);
    }
}
