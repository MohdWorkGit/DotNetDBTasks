using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ScheduledTaskViewerConfiguration : IEntityTypeConfiguration<ScheduledTaskViewer>
{
    public void Configure(EntityTypeBuilder<ScheduledTaskViewer> builder)
    {
        builder.HasKey(e => new { e.ScheduledTaskId, e.UserId });

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);
    }
}
