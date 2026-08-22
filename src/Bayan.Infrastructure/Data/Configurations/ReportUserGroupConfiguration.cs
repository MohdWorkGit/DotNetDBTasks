using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ReportUserGroupConfiguration : IEntityTypeConfiguration<ReportUserGroup>
{
    public void Configure(EntityTypeBuilder<ReportUserGroup> builder)
    {
        builder.HasKey(e => new { e.ReportId, e.UserGroupId });

        builder.HasOne(e => e.Report)
            .WithMany(r => r.ReportUserGroups)
            .HasForeignKey(e => e.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.UserGroup)
            .WithMany()
            .HasForeignKey(e => e.UserGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserGroupId);
    }
}
