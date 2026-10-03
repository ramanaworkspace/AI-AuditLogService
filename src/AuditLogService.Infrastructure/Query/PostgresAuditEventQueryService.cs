using AuditLogService.Application.Query;
using AuditLogService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditLogService.Infrastructure.Query;

/// <summary>
/// Queries audit events from PostgreSQL using keyset pagination on <c>sequence_number</c>.
/// </summary>
/// <remarks>
/// The <c>audit_events</c> table already has the indexes this service's filters rely on
/// (<c>ix_audit_events_actor_id</c>, <c>ix_audit_events_event_type</c>,
/// <c>ix_audit_events_resource_type_resource_id</c>, <c>ix_audit_events_timestamp</c>, and the
/// unique <c>ux_audit_events_sequence_number</c> used for ordering and the pagination cursor) -
/// see <see cref="AuditEventRecordConfiguration"/>.
/// </remarks>
public sealed class PostgresAuditEventQueryService(IDbContextFactory<AuditLogDbContext> dbContextFactory)
    : IAuditEventQueryService
{
    public async Task<AuditEventQueryResult> QueryAsync(
        AuditEventQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var records = dbContext.AuditEvents.AsNoTracking().AsQueryable();

        if (query.ActorId is not null)
        {
            records = records.Where(record => record.ActorId == query.ActorId);
        }

        if (query.ResourceType is not null)
        {
            records = records.Where(record => record.ResourceType == query.ResourceType);
        }

        if (query.ResourceId is not null)
        {
            records = records.Where(record => record.ResourceId == query.ResourceId);
        }

        if (query.EventType is not null)
        {
            records = records.Where(record => record.EventType == query.EventType);
        }

        if (query.StartTimestamp is not null)
        {
            var start = query.StartTimestamp.Value.UtcDateTime;
            records = records.Where(record => record.Timestamp >= start);
        }

        if (query.EndTimestamp is not null)
        {
            var end = query.EndTimestamp.Value.UtcDateTime;
            records = records.Where(record => record.Timestamp <= end);
        }

        if (query.AfterSequenceNumber is not null)
        {
            records = records.Where(record => record.SequenceNumber > query.AfterSequenceNumber.Value);
        }

        // Fetch one extra row so hasMore/the next cursor can be determined without a second
        // round trip.
        var page = await records
            .OrderBy(record => record.SequenceNumber)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > query.Limit;
        var items = (hasMore ? page.Take(query.Limit) : page)
            .Select(record => record.ToDomain())
            .ToList();

        var nextCursor = hasMore ? AuditEventCursor.Encode(items[^1].SequenceNumber) : null;

        var eventIds = items.Select(item => item.EventId).ToArray();
        var archives = await dbContext.AuditEventArchives.AsNoTracking()
            .Where(archive => eventIds.Contains(archive.EventId))
            .ToDictionaryAsync(archive => archive.EventId,
                archive => new DateTimeOffset(archive.ArchivedAt), cancellationToken);

        return new AuditEventQueryResult(items, nextCursor) { ArchivedAtByEventId = archives };
    }
}
