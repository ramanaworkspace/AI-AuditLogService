namespace AuditLogService.Application.Retention;

public sealed record ArchiveOperationResult(
    DateTimeOffset EligibilityCutoff,
    DateTimeOffset ArchivedAt,
    int RecordsArchived);
