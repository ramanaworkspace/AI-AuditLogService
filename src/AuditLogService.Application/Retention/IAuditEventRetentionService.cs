namespace AuditLogService.Application.Retention;

public interface IAuditEventRetentionService
{
    Task<AuditEventArchiveState> GetStateAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> ArchiveEligibleAsync(CancellationToken cancellationToken = default);
}
