using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class QueryExecutionLogConfiguration : IEntityTypeConfiguration<QueryExecutionLog>
{
    public void Configure(EntityTypeBuilder<QueryExecutionLog> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ParametersJson).HasColumnType("CLOB");
        builder.Property(e => e.OldValuesJson).HasColumnType("CLOB");
        builder.Property(e => e.ErrorMessage).HasMaxLength(1000);

        builder.HasOne(e => e.User)
            .WithMany(u => u.QueryExecutionLogs)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(e => e.ExecutedAt);
        builder.HasIndex(e => e.UserId);
    }
}
