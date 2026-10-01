using Microsoft.AspNetCore.Mvc;

namespace AuditLogService.Api.Contracts;

/// <summary>
/// Query-string parameters bound for <c>GET /api/v1/audit-events</c>.
/// </summary>
public sealed class AuditEventQueryParameters
{
    /// <summary>Filters results to events recorded by this actor.</summary>
    [FromQuery(Name = "actorId")]
    public string? ActorId { get; init; }

    /// <summary>Filters results to events for this resource type.</summary>
    [FromQuery(Name = "resourceType")]
    public string? ResourceType { get; init; }

    /// <summary>Filters results to events for this resource ID.</summary>
    [FromQuery(Name = "resourceId")]
    public string? ResourceId { get; init; }

    /// <summary>Filters results to this event type.</summary>
    [FromQuery(Name = "eventType")]
    public string? EventType { get; init; }

    /// <summary>Inclusive lower bound on the event timestamp.</summary>
    [FromQuery(Name = "startTimestamp")]
    public DateTimeOffset? StartTimestamp { get; init; }

    /// <summary>Inclusive upper bound on the event timestamp.</summary>
    [FromQuery(Name = "endTimestamp")]
    public DateTimeOffset? EndTimestamp { get; init; }

    /// <summary>An opaque pagination cursor returned by a previous page's <c>nextCursor</c>.</summary>
    [FromQuery(Name = "cursor")]
    public string? Cursor { get; init; }

    /// <summary>The maximum number of events to return in this page.</summary>
    [FromQuery(Name = "limit")]
    public int? Limit { get; init; }
}
