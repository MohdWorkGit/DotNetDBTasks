using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class QueryGroupUserGroupConfiguration : IEntityTypeConfiguration<QueryGroupUserGroup>
{
    public void Configure(EntityTypeBuilder<QueryGroupUserGroup> builder)
    {
        builder.HasKey(e => new { e.QueryGroupId, e.UserGroupId });

        builder.HasOne(e => e.QueryGroup)
            .WithMany(g => g.QueryGroupUserGroups)
            .HasForeignKey(e => e.QueryGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.UserGroup)
            .WithMany(g => g.QueryGroupUserGroups)
            .HasForeignKey(e => e.UserGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
