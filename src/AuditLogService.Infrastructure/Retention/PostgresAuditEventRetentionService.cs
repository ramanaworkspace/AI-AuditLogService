using AuditLogService.Application.Retention;
using AuditLogService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuditLogService.Infrastructure.Retention;

public sealed class PostgresAuditEventRetentionService(
    IDbContextFactory<AuditLogDbContext> dbContextFactory,
    IOptions<RetentionOptions> options,
    TimeProvider timeProvider) : IAuditEventRetentionService
{
    public async Task<AuditEventArchiveState> GetStateAsync(
        Guid eventId, CancellationToken cancellationToken = default)
    {
        var (_, cutoff) = GetEvaluationTime();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var state = await (
            from record in dbContext.AuditEvents.AsNoTracking()
            join archive in dbContext.AuditEventArchives.AsNoTracking()
                on record.EventId equals archive.EventId into archives
            from archive in archives.DefaultIfEmpty()
            where record.EventId == eventId
            select new { record.Timestamp, ArchivedAt = (DateTime?)archive.ArchivedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (state is null)
        {
            throw new KeyNotFoundException($"Audit event {eventId} does not exist.");
        }

        return new AuditEventArchiveState(
            eventId,
            state.Timestamp < cutoff.UtcDateTime,
            state.ArchivedAt.HasValue ? new DateTimeOffset(state.ArchivedAt.Value) : null);
    }

    public async Task<ArchiveOperationResult> ArchiveEligibleAsync(
        CancellationToken cancellationToken = default)
    {
        var (now, cutoff) = GetEvaluationTime();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // Stored timestamps are microsecond-aligned. Round the exclusive cutoff up,
        // preserving the exact comparison even for sub-microsecond window values.
        var remainder = cutoff.Ticks % TimeSpan.TicksPerMicrosecond;
        var databaseCutoff = remainder == 0
            ? cutoff : cutoff.AddTicks(TimeSpan.TicksPerMicrosecond - remainder);

        // One atomic statement; concurrent/repeated runs preserve the first archive timestamp.
        var archived = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO audit_event_archives (event_id, archived_at)
            SELECT event_id, {now.UtcDateTime}
            FROM audit_events
            WHERE timestamp < {databaseCutoff.UtcDateTime}
            ON CONFLICT (event_id) DO NOTHING
            """, cancellationToken);

        return new ArchiveOperationResult(cutoff, now, archived);
    }

    private (DateTimeOffset Now, DateTimeOffset Cutoff) GetEvaluationTime()
    {
        var window = options.Value.Window;
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Retention window must be positive.");
        }

        var now = timeProvider.GetUtcNow().ToUniversalTime();
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        if (window.Ticks > now.Ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Retention window exceeds the representable timestamp range.");
        }

        return (now, now.Subtract(window));
    }
}
