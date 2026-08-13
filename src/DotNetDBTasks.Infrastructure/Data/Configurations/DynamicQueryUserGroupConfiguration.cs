using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class DynamicQueryUserGroupConfiguration : IEntityTypeConfiguration<DynamicQueryUserGroup>
{
    public void Configure(EntityTypeBuilder<DynamicQueryUserGroup> builder)
    {
        builder.HasKey(e => new { e.DynamicQueryId, e.UserGroupId });

        builder.HasOne(e => e.DynamicQuery)
            .WithMany(q => q.DynamicQueryUserGroups)
            .HasForeignKey(e => e.DynamicQueryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.UserGroup)
            .WithMany(g => g.DynamicQueryUserGroups)
            .HasForeignKey(e => e.UserGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
