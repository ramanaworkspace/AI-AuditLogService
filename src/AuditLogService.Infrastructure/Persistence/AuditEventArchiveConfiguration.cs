using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuditLogService.Infrastructure.Persistence;

public sealed class AuditEventArchiveConfiguration : IEntityTypeConfiguration<AuditEventArchive>
{
    public void Configure(EntityTypeBuilder<AuditEventArchive> builder)
    {
        builder.ToTable("audit_event_archives");
        builder.HasKey(archive => archive.EventId).HasName("pk_audit_event_archives");
        builder.Property(archive => archive.EventId).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(archive => archive.ArchivedAt)
            .HasColumnName("archived_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<AuditEventRecord>().WithOne()
            .HasForeignKey<AuditEventArchive>(archive => archive.EventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
