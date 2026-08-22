using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ReportRoleConfiguration : IEntityTypeConfiguration<ReportRole>
{
    public void Configure(EntityTypeBuilder<ReportRole> builder)
    {
        builder.HasKey(e => new { e.ReportId, e.RoleId });

        builder.HasOne(e => e.Report)
            .WithMany(r => r.ReportRoles)
            .HasForeignKey(e => e.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.RoleId);
    }
}
