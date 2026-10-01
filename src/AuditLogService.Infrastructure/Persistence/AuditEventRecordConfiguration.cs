using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuditLogService.Infrastructure.Persistence;

public sealed class AuditEventRecordConfiguration : IEntityTypeConfiguration<AuditEventRecord>
{
    public void Configure(EntityTypeBuilder<AuditEventRecord> builder)
    {
        builder.ToTable("audit_events", table =>
        {
            table.HasCheckConstraint("ck_audit_events_sequence_positive", "sequence_number > 0");
            table.HasCheckConstraint("ck_audit_events_event_type_not_empty", "length(btrim(event_type)) > 0");
            table.HasCheckConstraint("ck_audit_events_actor_id_not_empty", "length(btrim(actor_id)) > 0");
            table.HasCheckConstraint("ck_audit_events_resource_type_not_empty", "length(btrim(resource_type)) > 0");
            table.HasCheckConstraint("ck_audit_events_resource_id_not_empty", "length(btrim(resource_id)) > 0");
            table.HasCheckConstraint("ck_audit_events_content_hash_format", "content_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "ck_audit_events_previous_hash_format",
                "previous_hash = 'GENESIS' OR previous_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_audit_events_payload_object", "jsonb_typeof(payload) = 'object'");
        });

        builder.HasKey(eventRecord => eventRecord.EventId)
            .HasName("pk_audit_events");
        builder.Property(eventRecord => eventRecord.EventId)
            .HasColumnName("event_id")
            .ValueGeneratedNever();
        builder.Property(eventRecord => eventRecord.SequenceNumber)
            .HasColumnName("sequence_number")
            .IsRequired();
        builder.HasIndex(eventRecord => eventRecord.SequenceNumber)
            .IsUnique()
            .HasDatabaseName("ux_audit_events_sequence_number");
        builder.Property(eventRecord => eventRecord.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(200)
            .IsRequired();
        builder.HasIndex(eventRecord => eventRecord.EventType)
            .HasDatabaseName("ix_audit_events_event_type");
        builder.Property(eventRecord => eventRecord.ActorId)
            .HasColumnName("actor_id")
            .HasMaxLength(300)
            .IsRequired();
        builder.HasIndex(eventRecord => eventRecord.ActorId)
            .HasDatabaseName("ix_audit_events_actor_id");
        builder.Property(eventRecord => eventRecord.ResourceType)
            .HasColumnName("resource_type")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(eventRecord => eventRecord.ResourceId)
            .HasColumnName("resource_id")
            .HasMaxLength(500)
            .IsRequired();
        builder.HasIndex(eventRecord => new { eventRecord.ResourceType, eventRecord.ResourceId })
            .HasDatabaseName("ix_audit_events_resource_type_resource_id");
        builder.Property(eventRecord => eventRecord.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(eventRecord => eventRecord.Timestamp)
            .HasColumnName("timestamp")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.HasIndex(eventRecord => eventRecord.Timestamp)
            .HasDatabaseName("ix_audit_events_timestamp");
        builder.Property(eventRecord => eventRecord.PreviousHash)
            .HasColumnName("previous_hash")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(eventRecord => eventRecord.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(64)
            .IsRequired();
    }
}
