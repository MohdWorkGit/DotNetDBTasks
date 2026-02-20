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
    }
}
