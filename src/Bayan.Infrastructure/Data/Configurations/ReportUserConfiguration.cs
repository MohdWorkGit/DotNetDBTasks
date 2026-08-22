using Bayan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bayan.Infrastructure.Data.Configurations;

public class ReportUserConfiguration : IEntityTypeConfiguration<ReportUser>
{
    public void Configure(EntityTypeBuilder<ReportUser> builder)
    {
        builder.HasKey(e => new { e.ReportId, e.UserId });

        builder.HasOne(e => e.Report)
            .WithMany(r => r.ReportUsers)
            .HasForeignKey(e => e.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);
    }
}
