using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ScheduledTaskViewerRoleConfiguration : IEntityTypeConfiguration<ScheduledTaskViewerRole>
{
    public void Configure(EntityTypeBuilder<ScheduledTaskViewerRole> builder)
    {
        builder.HasKey(e => new { e.ScheduledTaskId, e.RoleId });

        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.RoleId);
    }
}
