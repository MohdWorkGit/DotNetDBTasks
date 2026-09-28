using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class DashboardUserGroupConfiguration : IEntityTypeConfiguration<DashboardUserGroup>
{
    public void Configure(EntityTypeBuilder<DashboardUserGroup> builder)
    {
        builder.HasKey(e => new { e.DashboardId, e.UserGroupId });

        builder.HasOne(e => e.Dashboard)
            .WithMany(d => d.DashboardUserGroups)
            .HasForeignKey(e => e.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.UserGroup)
            .WithMany()
            .HasForeignKey(e => e.UserGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserGroupId);
    }
}
