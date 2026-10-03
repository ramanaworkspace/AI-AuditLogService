namespace AuditLogService.Application.Retention;

public sealed record AuditEventArchiveState(
    Guid EventId,
    bool IsEligible,
    DateTimeOffset? ArchivedAt)
{
    public bool IsArchived => ArchivedAt.HasValue;
}
