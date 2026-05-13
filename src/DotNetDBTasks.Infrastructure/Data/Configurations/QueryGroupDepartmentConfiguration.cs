using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class QueryGroupDepartmentConfiguration : IEntityTypeConfiguration<QueryGroupDepartment>
{
    public void Configure(EntityTypeBuilder<QueryGroupDepartment> builder)
    {
        builder.HasKey(e => new { e.QueryGroupId, e.Department });

        builder.Property(e => e.Department).HasMaxLength(200).IsRequired();

        builder.HasOne(e => e.QueryGroup)
            .WithMany(g => g.QueryGroupDepartments)
            .HasForeignKey(e => e.QueryGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
