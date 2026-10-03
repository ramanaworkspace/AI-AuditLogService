using System.Text.Json;
using AuditLogService.Domain;

namespace AuditLogService.Api.Contracts;

/// <summary>
/// The wire representation of a persisted audit event, including every chain field.
/// </summary>
public sealed record AuditEventResponse(
    Guid EventId,
    long SequenceNumber,
    string EventType,
    string ActorId,
    string ResourceType,
    string ResourceId,
    JsonElement Payload,
    DateTimeOffset Timestamp,
    string PreviousHash,
    string ContentHash)
{
    public DateTimeOffset? ArchivedAt { get; init; }

    public bool IsArchived => ArchivedAt.HasValue;

    public static AuditEventResponse FromDomain(AuditEvent auditEvent, DateTimeOffset? archivedAt = null)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        return new AuditEventResponse(
            auditEvent.EventId,
            auditEvent.SequenceNumber,
            auditEvent.EventType,
            auditEvent.ActorId,
            auditEvent.ResourceType,
            auditEvent.ResourceId,
            auditEvent.Payload,
            auditEvent.Timestamp,
            auditEvent.PreviousHash,
            auditEvent.ContentHash)
        { ArchivedAt = archivedAt };
    }
}
