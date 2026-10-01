namespace AuditLogService.Application.Query;

/// <summary>
/// Filter and pagination parameters for listing audit events.
/// </summary>
/// <remarks>
/// All filters are optional and are combined with logical AND. <see cref="StartTimestamp"/>
/// and <see cref="EndTimestamp"/> are both inclusive bounds. <see cref="AfterSequenceNumber"/>
/// is the decoded keyset-pagination cursor (see <see cref="AuditEventCursor"/>): when present,
/// only events with a strictly greater sequence number are returned.
/// </remarks>
public sealed record AuditEventQuery(
    string? ActorId,
    string? ResourceType,
    string? ResourceId,
    string? EventType,
    DateTimeOffset? StartTimestamp,
    DateTimeOffset? EndTimestamp,
    long? AfterSequenceNumber,
    int Limit);
