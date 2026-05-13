using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class QueryGroupUserConfiguration : IEntityTypeConfiguration<QueryGroupUser>
{
    public void Configure(EntityTypeBuilder<QueryGroupUser> builder)
    {
        builder.HasKey(e => new { e.QueryGroupId, e.UserId });

        builder.HasOne(e => e.QueryGroup)
            .WithMany(g => g.QueryGroupUsers)
            .HasForeignKey(e => e.QueryGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
