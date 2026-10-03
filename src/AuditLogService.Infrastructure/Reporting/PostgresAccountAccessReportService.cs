using System.Data;
using AuditLogService.Application.Redaction;
using AuditLogService.Application.Reporting;
using AuditLogService.Domain;
using AuditLogService.Infrastructure.Persistence;
using AuditLogService.Infrastructure.Verification;
using Microsoft.EntityFrameworkCore;

namespace AuditLogService.Infrastructure.Reporting;

public sealed class PostgresAccountAccessReportService(
    IDbContextFactory<AuditLogDbContext> contextFactory,
    IEventHasher hasher,
    IPayloadProtector protector) : IAccountAccessReportService
{
    public async Task<AccountAccessReport> CreateAsync(
        AccountAccessReportQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var head = await context.ChainMetadata.AsNoTracking()
            .SingleAsync(row => row.ChainId == ChainMetadata.GlobalChainId, cancellationToken);
        var verification = await PostgresChainVerificationService.VerifySnapshotAsync(context, hasher, cancellationToken);
        var start = query.StartTime.UtcDateTime;
        var end = query.EndTime.UtcDateTime;
        var records = await context.AuditEvents.AsNoTracking()
            .Where(row => row.ResourceType == AccountAccessPolicy.ResourceType
                && (row.EventType == AccountAccessPolicy.Succeeded
                    || row.EventType == AccountAccessPolicy.Denied || row.EventType == AccountAccessPolicy.Failed)
                && row.Timestamp >= start && row.Timestamp <= end)
            .OrderBy(row => row.SequenceNumber)
            .ToListAsync(cancellationToken);
        var ids = records.Select(row => row.EventId).ToArray();
        var archives = await context.AuditEventArchives.AsNoTracking()
            .Where(row => ids.Contains(row.EventId))
            .ToDictionaryAsync(row => row.EventId, row => row.ArchivedAt, cancellationToken);
        var items = records.Select(row => new AccountAccessReportItem(
            row.EventId, row.SequenceNumber, row.EventType,
            AccountAccessPolicy.Outcome(row.EventType)
                ?? throw new InvalidOperationException("Unexpected account-access event type."),
            row.ActorId, row.ResourceType, row.ResourceId, protector.Project(row.Payload),
            AccountAccessReport.UtcText(new DateTimeOffset(row.Timestamp)),
            row.PreviousHash, row.ContentHash, archives.ContainsKey(row.EventId),
            archives.TryGetValue(row.EventId, out var archivedAt)
                ? AccountAccessReport.UtcText(new DateTimeOffset(archivedAt)) : null)).ToArray();
        await transaction.CommitAsync(cancellationToken);
        return new AccountAccessReport("account-access-v1", AccountAccessPolicy.ResourceType,
            AccountAccessReport.UtcText(query.StartTime), AccountAccessReport.UtcText(query.EndTime),
            new AccountAccessChainStatus(verification.IsValid, verification.EventsVerified,
                verification.ArchivedEventsVerified, head.HeadSequenceNumber, head.HeadHash,
                verification.FirstInconsistentEventId, verification.FirstInconsistentSequenceNumber,
                verification.ViolationType?.ToString(), verification.Detail), items);
    }
}
