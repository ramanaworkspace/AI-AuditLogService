using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuditLogService.Infrastructure.Persistence;

public sealed class AuditEventReadProjectionConfiguration : IEntityTypeConfiguration<AuditEventReadProjection>
{
    public void Configure(EntityTypeBuilder<AuditEventReadProjection> builder)
    {
        builder.ToTable("audit_event_read_projections");
        builder.HasKey(projection => projection.EventId);
        builder.Property(projection => projection.EventId).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(projection => projection.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.HasOne<AuditEventRecord>().WithOne()
            .HasForeignKey<AuditEventReadProjection>(projection => projection.EventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
