using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class QueryParameterConfiguration : IEntityTypeConfiguration<QueryParameter>
{
    public void Configure(EntityTypeBuilder<QueryParameter> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DefaultValue).HasMaxLength(500);

        // Dropdown-specific columns
        // The Oracle provider stores .NET bool as NUMBER(1) (mapped to short
        // internally), and any model-level default value blows up during migration
        // with an Int16→Bool cast error. The migration handles the existing-row
        // backfill at the SQL level instead.
        builder.Property(e => e.AllowMultiple).HasColumnType("NUMBER(1)");
        builder.Property(e => e.DropdownStaticValues).HasColumnType("CLOB");
        builder.Property(e => e.DropdownQueryValueColumn).HasMaxLength(100);
        builder.Property(e => e.DropdownQueryLabelColumn).HasMaxLength(100);

        // DropdownQueryId is a plain foreign-key value — no navigation property or DB constraint.
        // Oracle EF Core does not reliably support ON DELETE SET NULL.
        // The lookup query is resolved by the application layer, which handles missing queries gracefully.
        builder.Property(e => e.DropdownQueryId).HasColumnType("RAW(16)");
        builder.HasIndex(e => e.DropdownQueryId).HasDatabaseName("IX_QueryParameters_DropdownQueryId");
    }
}
