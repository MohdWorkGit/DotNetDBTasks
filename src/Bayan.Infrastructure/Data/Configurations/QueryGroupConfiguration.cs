using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class QueryGroupConfiguration : IEntityTypeConfiguration<QueryGroup>
{
    public void Configure(EntityTypeBuilder<QueryGroup> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000).IsRequired();

        builder.HasIndex(e => e.Name).IsUnique();

        builder.HasMany(e => e.DynamicQueries)
            .WithOne(q => q.QueryGroup)
            .HasForeignKey(q => q.QueryGroupId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
