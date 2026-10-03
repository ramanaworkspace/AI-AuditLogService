namespace AuditLogService.Infrastructure.Persistence;

public sealed class AuditEventArchive
{
    public Guid EventId { get; set; }

    public DateTime ArchivedAt { get; set; }
}
