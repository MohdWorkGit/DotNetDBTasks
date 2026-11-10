using Jadwal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jadwal.Infrastructure.Data.Configurations;

public class QueryGroupQueryConfiguration : IEntityTypeConfiguration<QueryGroupQuery>
{
    public void Configure(EntityTypeBuilder<QueryGroupQuery> builder)
    {
        builder.HasKey(e => new { e.QueryGroupId, e.DynamicQueryId });

        // Deleting either side removes only the filing: a deleted group leaves its queries in
        // their other groups (or ungrouped), and a deleted query leaves the group in place.
        builder.HasOne(e => e.QueryGroup)
            .WithMany(g => g.QueryGroupQueries)
            .HasForeignKey(e => e.QueryGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.DynamicQuery)
            .WithMany(q => q.QueryGroupQueries)
            .HasForeignKey(e => e.DynamicQueryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
