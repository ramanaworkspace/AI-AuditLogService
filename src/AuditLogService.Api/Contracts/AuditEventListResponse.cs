namespace AuditLogService.Api.Contracts;

/// <summary>
/// Response body for <c>GET /api/v1/audit-events</c>: a single page of results plus the
/// cursor for the next page.
/// </summary>
public sealed record AuditEventListResponse(
    IReadOnlyList<AuditEventResponse> Items,
    string? NextCursor);
