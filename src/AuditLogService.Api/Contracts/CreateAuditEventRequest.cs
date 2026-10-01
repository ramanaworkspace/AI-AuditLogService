using System.Text.Json;

namespace AuditLogService.Api.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/audit-events</c>.
/// </summary>
/// <remarks>
/// Deliberately excludes <c>EventId</c>, <c>SequenceNumber</c>, <c>Timestamp</c>,
/// <c>PreviousHash</c>, and <c>ContentHash</c>: these are chain-critical fields assigned
/// exclusively by the server-side append process. System.Text.Json ignores unknown JSON
/// properties by default, so a client that includes any of those fields in the request body
/// has them silently dropped rather than honored.
/// </remarks>
public sealed record CreateAuditEventRequest(
    string? EventType,
    string? ActorId,
    string? ResourceType,
    string? ResourceId,
    JsonElement Payload);
