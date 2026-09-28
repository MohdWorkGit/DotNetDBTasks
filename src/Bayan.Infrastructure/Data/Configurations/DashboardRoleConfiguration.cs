using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class DashboardRoleConfiguration : IEntityTypeConfiguration<DashboardRole>
{
    public void Configure(EntityTypeBuilder<DashboardRole> builder)
    {
        builder.HasKey(e => new { e.DashboardId, e.RoleId });

        builder.HasOne(e => e.Dashboard)
            .WithMany(d => d.DashboardRoles)
            .HasForeignKey(e => e.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.RoleId);
    }
}
