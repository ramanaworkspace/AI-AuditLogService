namespace AuditLogService.Application.Query;

/// <summary>
/// Queries persisted audit events with filtering and keyset pagination.
/// </summary>
public interface IAuditEventQueryService
{
    /// <summary>
    /// Returns a single page of audit events matching <paramref name="query"/>, ordered
    /// ascending by sequence number.
    /// </summary>
    Task<AuditEventQueryResult> QueryAsync(AuditEventQuery query, CancellationToken cancellationToken = default);
}
