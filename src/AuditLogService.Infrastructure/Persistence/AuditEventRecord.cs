using System.Text.Json;
using AuditLogService.Domain;

namespace AuditLogService.Infrastructure.Persistence;

public sealed class AuditEventRecord
{
    public Guid EventId { get; set; }

    public long SequenceNumber { get; set; }

    public required string EventType { get; set; }

    public required string ActorId { get; set; }

    public required string ResourceType { get; set; }

    public required string ResourceId { get; set; }

    public JsonElement Payload { get; set; }

    public DateTime Timestamp { get; set; }

    public required string PreviousHash { get; set; }

    public required string ContentHash { get; set; }

    public static AuditEventRecord FromDomain(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        return new AuditEventRecord
        {
            EventId = auditEvent.EventId,
            SequenceNumber = auditEvent.SequenceNumber,
            EventType = auditEvent.EventType,
            ActorId = auditEvent.ActorId,
            ResourceType = auditEvent.ResourceType,
            ResourceId = auditEvent.ResourceId,
            Payload = auditEvent.Payload.Clone(),
            Timestamp = auditEvent.Timestamp.UtcDateTime,
            PreviousHash = auditEvent.PreviousHash,
            ContentHash = auditEvent.ContentHash
        };
    }

    public AuditEvent ToDomain() =>
        new(
            new AuditEventData(
                EventId,
                SequenceNumber,
                EventType,
                ActorId,
                ResourceType,
                ResourceId,
                Payload,
                new DateTimeOffset(DateTime.SpecifyKind(Timestamp, DateTimeKind.Utc)),
                PreviousHash),
            ContentHash);
}
