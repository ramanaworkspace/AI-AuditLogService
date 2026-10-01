using AuditLogService.Application.Append;
using AuditLogService.Domain;
using AuditLogService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditLogService.Infrastructure.Append;

/// <summary>
/// Appends audit events using a single PostgreSQL transaction guarded by a
/// transaction-scoped advisory lock (plan.md: "global PostgreSQL transaction-scoped
/// advisory lock").
/// </summary>
/// <remarks>
/// <para>
/// Per append, this service performs exactly one database transaction, in order:
/// </para>
/// <list type="number">
/// <item>Acquire <c>pg_advisory_xact_lock(ChainAdvisoryLock.Key)</c>.</item>
/// <item>Read the current chain head (<see cref="ChainMetadata"/>).</item>
/// <item>Determine the next sequence number (head + 1).</item>
/// <item>Determine the previous hash (the head's content hash, or <c>GENESIS</c>).</item>
/// <item>Construct the immutable event (server-assigned <see cref="AuditEventData.EventId"/>
/// and <see cref="AuditEventData.Timestamp"/>; clients never supply these).</item>
/// <item>Canonically serialize it and compute its content hash (<see cref="AuditEvent.Create"/>).</item>
/// <item>Insert the event row.</item>
/// <item>Update the chain-head row to point at the new event.</item>
/// <item>Commit.</item>
/// </list>
/// <para>
/// The advisory lock is transaction-scoped: PostgreSQL releases it automatically on
/// commit or rollback, so it cannot be leaked by a crashed process and never needs
/// explicit release code. If any step above throws, the transaction is never committed;
/// disposing it (via <c>await using</c>) rolls it back, so the event insert and the
/// chain-head update are never applied, leaving the chain exactly as it was before the
/// call. Because the lock serializes every append against the same chain, the sequence
/// number and previous hash read in steps 2-4 are guaranteed to still be correct at
/// commit time - there is no read-then-write race window for another transaction to
/// invalidate them.
/// </para>
/// <para>See docs/concurrency.md for the full design rationale and trade-offs.</para>
/// </remarks>
public sealed class PostgresAuditEventAppendService(
    IDbContextFactory<AuditLogDbContext> dbContextFactory,
    IEventHasher eventHasher) : IAuditEventAppendService
{
    public async Task<AuditEvent> AppendAsync(
        AppendAuditEventRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // 1. Acquire the transaction-scoped advisory lock. This blocks until any other
        // concurrent append transaction commits or rolls back, guaranteeing that only
        // one transaction at a time reads and advances the chain head.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({ChainAdvisoryLock.Key})",
            cancellationToken);

        // 2. Read the current chain head. Safe from concurrent mutation now that the
        // advisory lock above serializes every other append against this same row.
        var chainMetadata = await dbContext.ChainMetadata
            .SingleAsync(metadata => metadata.ChainId == ChainMetadata.GlobalChainId, cancellationToken);

        // 3 & 4. Determine the next sequence number and previous hash from the head.
        var nextSequenceNumber = chainMetadata.HeadSequenceNumber + 1;
        var previousHash = chainMetadata.HeadHash;

        // 5, 6 & 7. Construct the immutable event (server-assigned EventId and
        // Timestamp), canonically serialize it, and compute its content hash.
        var eventData = new AuditEventData(
            Guid.NewGuid(),
            nextSequenceNumber,
            request.EventType,
            request.ActorId,
            request.ResourceType,
            request.ResourceId,
            request.Payload,
            DateTimeOffset.UtcNow,
            previousHash);
        var auditEvent = AuditEvent.Create(eventData, eventHasher);

        // 8. Insert the event.
        dbContext.AuditEvents.Add(AuditEventRecord.FromDomain(auditEvent));

        // 9. Update chain-head metadata (tracked entity; applied by SaveChangesAsync
        // together with the insert above, in the same round trip).
        chainMetadata.HeadSequenceNumber = auditEvent.SequenceNumber;
        chainMetadata.HeadHash = auditEvent.ContentHash;

        await dbContext.SaveChangesAsync(cancellationToken);

        // 10. Commit. If any step above throws, this line is never reached: the
        // `await using` transaction rolls back automatically on dispose, so the insert
        // and the chain-head update are both discarded together.
        await transaction.CommitAsync(cancellationToken);

        return auditEvent;
    }
}
