using System.Data;
using AuditLogService.Application.Verification;
using AuditLogService.Domain;
using AuditLogService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditLogService.Infrastructure.Verification;

/// <summary>
/// Verifies the persisted hash chain by loading every event, ordered by sequence number, and
/// delegating the consistency rules to <see cref="ChainVerifier"/>.
/// </summary>
public sealed class PostgresChainVerificationService(
    IDbContextFactory<AuditLogDbContext> dbContextFactory,
    IEventHasher eventHasher) : IChainVerificationService
{
    public async Task<ChainVerificationResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // Events and metadata must describe one snapshot, even during concurrent appends.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var result = await VerifySnapshotAsync(dbContext, eventHasher, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    internal static async Task<ChainVerificationResult> VerifySnapshotAsync(
        AuditLogDbContext dbContext, IEventHasher eventHasher, CancellationToken cancellationToken)
    {
        var head = await dbContext.ChainMetadata
            .AsNoTracking()
            .SingleAsync(metadata => metadata.ChainId == ChainMetadata.GlobalChainId, cancellationToken);

        var records = await dbContext.AuditEvents
            .AsNoTracking()
            .OrderBy(record => record.SequenceNumber)
            .ToListAsync(cancellationToken);

        var result = ChainVerifier.Verify(records.Select(record => record.ToDomain()), eventHasher);
        var archivedCount = await (
            from archive in dbContext.AuditEventArchives
            join record in dbContext.AuditEvents on archive.EventId equals record.EventId
            where record.SequenceNumber <= result.EventsVerified
            select archive.EventId).LongCountAsync(cancellationToken);
        result = result with { ArchivedEventsVerified = archivedCount };

        if (result.ViolationType == ChainViolationType.SequenceGap
            && records.Count < head.HeadSequenceNumber)
        {
            return MissingRecord(result.EventsVerified) with { ArchivedEventsVerified = archivedCount };
        }

        if (!result.IsValid)
        {
            return result;
        }

        if (result.EventsVerified < head.HeadSequenceNumber)
        {
            return MissingRecord(result.EventsVerified) with { ArchivedEventsVerified = archivedCount };
        }

        var tip = records.LastOrDefault();
        var tipHash = tip?.ContentHash ?? HashChain.GenesisHash;
        if (result.EventsVerified != head.HeadSequenceNumber
            || !string.Equals(tipHash, head.HeadHash, StringComparison.Ordinal))
        {
            return ChainVerificationResult.Invalid(
                result.EventsVerified,
                tip?.EventId,
                tip?.SequenceNumber ?? 0,
                ChainViolationType.ChainHeadMismatch,
                "The verified chain tip does not match the persisted chain-head metadata.") with
            { ArchivedEventsVerified = archivedCount };
        }

        return result;
    }

    private static ChainVerificationResult MissingRecord(long verifiedCount) =>
        ChainVerificationResult.Invalid(
            verifiedCount,
            null,
            verifiedCount + 1,
            ChainViolationType.MissingRecord,
            $"Expected record at sequence number {verifiedCount + 1} is absent relative to the persisted chain head; its event identifier is unavailable.");
}
