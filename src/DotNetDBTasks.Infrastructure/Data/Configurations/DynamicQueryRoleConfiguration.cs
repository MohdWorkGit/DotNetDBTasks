using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class DynamicQueryRoleConfiguration : IEntityTypeConfiguration<DynamicQueryRole>
{
    public void Configure(EntityTypeBuilder<DynamicQueryRole> builder)
    {
        builder.HasKey(e => new { e.DynamicQueryId, e.RoleId });

        builder.HasOne(e => e.DynamicQuery)
            .WithMany(q => q.DynamicQueryRoles)
            .HasForeignKey(e => e.DynamicQueryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Role)
            .WithMany(r => r.DynamicQueryRoles)
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
