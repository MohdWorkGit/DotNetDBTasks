using DotNetDBTasks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetDBTasks.Infrastructure.Data.Configurations;

public class SystemAuditLogConfiguration : IEntityTypeConfiguration<SystemAuditLog>
{
    public void Configure(EntityTypeBuilder<SystemAuditLog> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Username).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Category).HasMaxLength(50).IsRequired();
        builder.Property(e => e.EntityId).HasMaxLength(200);
        builder.Property(e => e.EntityName).HasMaxLength(400);
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);
        builder.Property(e => e.IpAddress).HasMaxLength(64);

        // 2000 is NVARCHAR2's ceiling on Oracle, and it is stated explicitly so the limit
        // lives next to the writer that enforces it (AuditLoggingBehavior.MaxDetailsLength).
        // A payload over the limit is replaced wholesale with a marker rather than cut,
        // because half a JSON document looks parseable and is not. CLOB was considered and
        // rejected: it complicates the LIKE-based search filter for a field that only ever
        // holds a short summary.
        builder.Property(e => e.DetailsJson).HasMaxLength(2000);

        // The page always sorts newest-first and usually filters on one of these, so each
        // gets an index. OccurredAt is the one that matters — without it every page load
        // is a full scan of a table that only ever grows.
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => e.Category);
        builder.HasIndex(e => e.UserId);

        // No FK to Users on purpose. The row must survive the account being deleted, and
        // Username/EntityName are already denormalized for exactly that reason.
    }
}
