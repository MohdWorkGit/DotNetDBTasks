using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class DynamicQueryConfiguration : IEntityTypeConfiguration<DynamicQuery>
{
    public void Configure(EntityTypeBuilder<DynamicQuery> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.SqlQuery).HasColumnType("CLOB").IsRequired();
        builder.Property(e => e.TimeoutSeconds).HasDefaultValue(30);

        builder.HasMany(e => e.Parameters)
            .WithOne(p => p.DynamicQuery)
            .HasForeignKey(p => p.DynamicQueryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.ExecutionLogs)
            .WithOne(l => l.DynamicQuery)
            .HasForeignKey(l => l.DynamicQueryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.DatabaseUser)
            .WithMany(d => d.DynamicQueries)
            .HasForeignKey(e => e.DatabaseUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.QueryGroup)
            .WithMany(g => g.DynamicQueries)
            .HasForeignKey(e => e.QueryGroupId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
