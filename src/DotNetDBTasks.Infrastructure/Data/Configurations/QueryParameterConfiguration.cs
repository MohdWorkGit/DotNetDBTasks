using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class QueryParameterConfiguration : IEntityTypeConfiguration<QueryParameter>
{
    public void Configure(EntityTypeBuilder<QueryParameter> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DefaultValue).HasMaxLength(500);

        // Dropdown-specific columns
        builder.Property(e => e.DropdownStaticValues).HasColumnType("CLOB");
        builder.Property(e => e.DropdownQueryValueColumn).HasMaxLength(100);
        builder.Property(e => e.DropdownQueryLabelColumn).HasMaxLength(100);

        // FK to the lookup query (when DropdownSourceType == Query)
        builder.HasOne(e => e.DropdownQuery)
            .WithMany()
            .HasForeignKey(e => e.DropdownQueryId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}
