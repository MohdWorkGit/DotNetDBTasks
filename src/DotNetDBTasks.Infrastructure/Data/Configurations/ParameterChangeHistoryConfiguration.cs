using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class ParameterChangeHistoryConfiguration : IEntityTypeConfiguration<ParameterChangeHistory>
{
    public void Configure(EntityTypeBuilder<ParameterChangeHistory> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ParameterName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ChangeType).HasMaxLength(20).IsRequired();
        builder.Property(e => e.FieldName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.OldValue).HasColumnType("CLOB");
        builder.Property(e => e.NewValue).HasColumnType("CLOB");
        builder.Property(e => e.ChangedByUserId).HasColumnType("RAW(16)");

        builder.HasIndex(e => e.DynamicQueryId)
            .HasDatabaseName("IX_ParameterChangeHistories_DynamicQueryId");

        builder.HasIndex(e => e.ChangedAt)
            .HasDatabaseName("IX_ParameterChangeHistories_ChangedAt");
    }
}
