using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ScheduledTaskViewerUserGroupConfiguration : IEntityTypeConfiguration<ScheduledTaskViewerUserGroup>
{
    public void Configure(EntityTypeBuilder<ScheduledTaskViewerUserGroup> builder)
    {
        builder.HasKey(e => new { e.ScheduledTaskId, e.UserGroupId });

        builder.HasOne(e => e.UserGroup)
            .WithMany()
            .HasForeignKey(e => e.UserGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserGroupId);
    }
}
