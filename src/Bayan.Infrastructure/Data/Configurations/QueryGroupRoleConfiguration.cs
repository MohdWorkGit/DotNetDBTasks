using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class QueryGroupRoleConfiguration : IEntityTypeConfiguration<QueryGroupRole>
{
    public void Configure(EntityTypeBuilder<QueryGroupRole> builder)
    {
        builder.HasKey(e => new { e.QueryGroupId, e.RoleId });

        builder.HasOne(e => e.QueryGroup)
            .WithMany(g => g.QueryGroupRoles)
            .HasForeignKey(e => e.QueryGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
