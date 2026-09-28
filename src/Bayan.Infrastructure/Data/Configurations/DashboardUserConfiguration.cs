using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class DashboardUserConfiguration : IEntityTypeConfiguration<DashboardUser>
{
    public void Configure(EntityTypeBuilder<DashboardUser> builder)
    {
        builder.HasKey(e => new { e.DashboardId, e.UserId });

        builder.HasOne(e => e.Dashboard)
            .WithMany(d => d.DashboardUsers)
            .HasForeignKey(e => e.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);
    }
}
