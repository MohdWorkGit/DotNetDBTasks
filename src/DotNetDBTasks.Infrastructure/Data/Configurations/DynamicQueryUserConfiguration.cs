using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class DynamicQueryUserConfiguration : IEntityTypeConfiguration<DynamicQueryUser>
{
    public void Configure(EntityTypeBuilder<DynamicQueryUser> builder)
    {
        builder.HasKey(e => new { e.DynamicQueryId, e.UserId });

        builder.HasOne(e => e.DynamicQuery)
            .WithMany(q => q.DynamicQueryUsers)
            .HasForeignKey(e => e.DynamicQueryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
