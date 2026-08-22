using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

/// <summary>
/// Mirrors <see cref="QueryParameterConfiguration"/> field for field, so the same client
/// controls and the same typed-coercion logic work against either kind of parameter.
/// </summary>
public class ReportParameterConfiguration : IEntityTypeConfiguration<ReportParameter>
{
    public void Configure(EntityTypeBuilder<ReportParameter> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DefaultValue).HasMaxLength(500);

        builder.Property(e => e.DropdownStaticValues).HasColumnType("CLOB");
        builder.Property(e => e.DropdownQueryValueColumn).HasMaxLength(100);
        builder.Property(e => e.DropdownQueryLabelColumn).HasMaxLength(100);

        // Plain foreign-key value, no navigation or DB constraint — the lookup query is
        // resolved at runtime and a missing one is handled gracefully, exactly as for
        // QueryParameter.DropdownQueryId.
        builder.Property(e => e.DropdownQueryId).HasColumnType("RAW(16)");
        builder.HasIndex(e => e.DropdownQueryId).HasDatabaseName("IX_ReportParameters_DropdownQueryId");

        builder.HasIndex(e => e.ReportId);
    }
}
