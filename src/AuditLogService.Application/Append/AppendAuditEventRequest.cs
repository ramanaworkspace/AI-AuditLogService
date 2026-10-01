using System.Text.Json;

namespace AuditLogService.Application.Append;

/// <summary>
/// Caller-supplied input for appending a new audit event to the chain.
/// </summary>
/// <remarks>
/// <see cref="AuditLogService.Domain.AuditEventData.EventId"/>, SequenceNumber, Timestamp, and
/// PreviousHash are intentionally absent here: they are chain-critical fields assigned
/// exclusively by the append process (see plan.md - "clients must not control the chain
/// sequence"). Callers only provide the descriptive content of the event.
/// </remarks>
public sealed record AppendAuditEventRequest(
    string EventType,
    string ActorId,
    string ResourceType,
    string ResourceId,
    JsonElement Payload);
