using System.Text.Json;
using AuditLogService.Domain;

namespace AuditLogService.Application.Redaction;

/// <summary>Safe event projection for future exporters; not a verifiable export bundle.</summary>
public sealed class AuditEventExportProjector(IPayloadProtector protector) : IAuditEventExportProjector
{
    public JsonElement Project(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        return JsonSerializer.SerializeToElement(new
        {
            eventId = auditEvent.EventId,
            sequenceNumber = auditEvent.SequenceNumber,
            eventType = auditEvent.EventType,
            actorId = auditEvent.ActorId,
            resourceType = auditEvent.ResourceType,
            resourceId = auditEvent.ResourceId,
            payload = protector.Project(auditEvent.Payload),
            timestamp = auditEvent.Timestamp,
            previousHash = auditEvent.PreviousHash,
            contentHash = auditEvent.ContentHash
        });
    }
}
