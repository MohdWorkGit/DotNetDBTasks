using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class DynamicQueryDepartmentConfiguration : IEntityTypeConfiguration<DynamicQueryDepartment>
{
    public void Configure(EntityTypeBuilder<DynamicQueryDepartment> builder)
    {
        builder.HasKey(e => new { e.DynamicQueryId, e.Department });

        builder.Property(e => e.Department).HasMaxLength(200).IsRequired();

        builder.HasOne(e => e.DynamicQuery)
            .WithMany(q => q.DynamicQueryDepartments)
            .HasForeignKey(e => e.DynamicQueryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
