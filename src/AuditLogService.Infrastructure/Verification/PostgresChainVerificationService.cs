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

        var records = await dbContext.AuditEvents
            .AsNoTracking()
            .OrderBy(record => record.SequenceNumber)
            .ToListAsync(cancellationToken);

        return ChainVerifier.Verify(records.Select(record => record.ToDomain()), eventHasher);
    }
}
