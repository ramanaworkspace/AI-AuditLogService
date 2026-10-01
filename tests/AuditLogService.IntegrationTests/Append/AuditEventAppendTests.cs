using System.Text.Json;
using AuditLogService.Application.Append;
using AuditLogService.Domain;
using AuditLogService.Infrastructure.Persistence;
using AuditLogService.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditLogService.IntegrationTests.Append;

/// <summary>
/// Integration tests for <see cref="PostgresAuditEventAppendService"/> - the atomic,
/// advisory-lock-guarded append operation described in docs/concurrency.md.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class AuditEventAppendTests(PostgreSqlFixture fixture)
{
    private readonly PostgreSqlFixture _fixture = fixture;

    [Fact]
    public async Task FirstAppendStartsFromGenesis()
    {
        await _fixture.ResetAsync();
        var service = _fixture.CreateAppendService();

        var appended = await service.AppendAsync(CreateRequest(actorId: "actor-1"));

        Assert.Equal(1, appended.SequenceNumber);
        Assert.Equal(HashChain.GenesisHash, appended.PreviousHash);
        Assert.True(HashChain.IsValidHash(appended.ContentHash));
    }

    [Fact]
    public async Task MultipleSequentialAppendsProduceCorrectSequenceNumbers()
    {
        await _fixture.ResetAsync();
        var service = _fixture.CreateAppendService();

        var first = await service.AppendAsync(CreateRequest(actorId: "actor-1"));
        var second = await service.AppendAsync(CreateRequest(actorId: "actor-2"));
        var third = await service.AppendAsync(CreateRequest(actorId: "actor-3"));

        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(2, second.SequenceNumber);
        Assert.Equal(3, third.SequenceNumber);
    }

    [Fact]
    public async Task EachAppendsPreviousHashMatchesThePriorRecordsContentHash()
    {
        await _fixture.ResetAsync();
        var service = _fixture.CreateAppendService();

        var first = await service.AppendAsync(CreateRequest(actorId: "actor-1"));
        var second = await service.AppendAsync(CreateRequest(actorId: "actor-2"));
        var third = await service.AppendAsync(CreateRequest(actorId: "actor-3"));

        Assert.Equal(HashChain.GenesisHash, first.PreviousHash);
        Assert.Equal(first.ContentHash, second.PreviousHash);
        Assert.Equal(second.ContentHash, third.PreviousHash);
    }

    [Fact]
    public async Task EachAppendsContentHashMatchesAnIndependentlyRecomputedHash()
    {
        await _fixture.ResetAsync();
        var service = _fixture.CreateAppendService();
        var serializer = new CanonicalEventSerializer();
        var hasher = new Sha256EventHasher(serializer);

        var first = await service.AppendAsync(CreateRequest(actorId: "actor-1"));
        var second = await service.AppendAsync(CreateRequest(actorId: "actor-2"));

        var recomputedFirst = hasher.ComputeHash(ToData(first));
        var recomputedSecond = hasher.ComputeHash(ToData(second));

        Assert.Equal(first.ContentHash, recomputedFirst);
        Assert.Equal(second.ContentHash, recomputedSecond);
    }

    [Fact]
    public async Task AppendedEventsArePersistedAndChainHeadIsUpdated()
    {
        await _fixture.ResetAsync();
        var service = _fixture.CreateAppendService();

        var appended = await service.AppendAsync(CreateRequest(actorId: "actor-1"));

        await using var verificationContext = _fixture.CreateDbContext();
        var persisted = await verificationContext.AuditEvents.SingleAsync();
        var chainMetadata = await verificationContext.ChainMetadata
            .SingleAsync(metadata => metadata.ChainId == ChainMetadata.GlobalChainId);

        Assert.Equal(appended.EventId, persisted.EventId);
        Assert.Equal(appended.ContentHash, persisted.ContentHash);
        Assert.Equal(appended.SequenceNumber, chainMetadata.HeadSequenceNumber);
        Assert.Equal(appended.ContentHash, chainMetadata.HeadHash);
    }

    [Fact]
    public async Task FailedAppendOnAnEmptyChainLeavesChainAtGenesis()
    {
        await _fixture.ResetAsync();
        var service = _fixture.CreateAppendService();

        // ActorId column is capped at 300 characters (AuditEventRecordConfiguration);
        // the domain layer does not enforce a max length, so this value passes domain
        // validation but is rejected by PostgreSQL at insert time (22001 - string data
        // right truncation), forcing the whole transaction to roll back.
        await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => service.AppendAsync(CreateRequest(actorId: new string('a', 301))));

        await using var verificationContext = _fixture.CreateDbContext();
        var chainMetadata = await verificationContext.ChainMetadata
            .SingleAsync(metadata => metadata.ChainId == ChainMetadata.GlobalChainId);

        Assert.Equal(0, chainMetadata.HeadSequenceNumber);
        Assert.Equal(HashChain.GenesisHash, chainMetadata.HeadHash);
        Assert.False(await verificationContext.AuditEvents.AnyAsync());
    }

    [Fact]
    public async Task FailedAppendAfterASuccessfulAppendRollsBackToThePriorValidState()
    {
        await _fixture.ResetAsync();
        var service = _fixture.CreateAppendService();

        var first = await service.AppendAsync(CreateRequest(actorId: "actor-1"));

        await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => service.AppendAsync(CreateRequest(actorId: new string('a', 301))));

        await using var verificationContext = _fixture.CreateDbContext();
        var chainMetadata = await verificationContext.ChainMetadata
            .SingleAsync(metadata => metadata.ChainId == ChainMetadata.GlobalChainId);
        var events = await verificationContext.AuditEvents.ToListAsync();

        Assert.Single(events);
        Assert.Equal(first.SequenceNumber, chainMetadata.HeadSequenceNumber);
        Assert.Equal(first.ContentHash, chainMetadata.HeadHash);

        // The chain must still be appendable after the rollback: the next append
        // continues from the last valid state rather than from a corrupted one.
        var third = await service.AppendAsync(CreateRequest(actorId: "actor-3"));
        Assert.Equal(2, third.SequenceNumber);
        Assert.Equal(first.ContentHash, third.PreviousHash);
    }

    [Fact]
    public async Task ConcurrentAppendsProduceAGaplessUnbrokenChain()
    {
        await _fixture.ResetAsync();
        const int concurrentAppendCount = 10;

        var tasks = Enumerable.Range(0, concurrentAppendCount)
            .Select(index => _fixture.CreateAppendService()
                .AppendAsync(CreateRequest(actorId: $"actor-{index}")))
            .ToArray();

        await Task.WhenAll(tasks);

        await using var verificationContext = _fixture.CreateDbContext();
        var events = await verificationContext.AuditEvents
            .OrderBy(record => record.SequenceNumber)
            .ToListAsync();
        var chainMetadata = await verificationContext.ChainMetadata
            .SingleAsync(metadata => metadata.ChainId == ChainMetadata.GlobalChainId);

        // No duplicate or missing sequence numbers: 1..N exactly, each appearing once.
        Assert.Equal(
            Enumerable.Range(1, concurrentAppendCount).Select(value => (long)value),
            events.Select(record => record.SequenceNumber));

        // Unbroken chain: each record's PreviousHash equals the prior record's
        // ContentHash, and the first record's PreviousHash is GENESIS.
        Assert.Equal(HashChain.GenesisHash, events[0].PreviousHash);
        for (var i = 1; i < events.Count; i++)
        {
            Assert.Equal(events[i - 1].ContentHash, events[i].PreviousHash);
        }

        Assert.Equal(concurrentAppendCount, chainMetadata.HeadSequenceNumber);
        Assert.Equal(events[^1].ContentHash, chainMetadata.HeadHash);
    }

    private static AppendAuditEventRequest CreateRequest(string actorId)
    {
        using var payloadDocument = JsonDocument.Parse("""{"test":true}""");
        return new AppendAuditEventRequest(
            "RECORD_UPDATED",
            actorId,
            "DOCUMENT",
            "document-1",
            payloadDocument.RootElement.Clone());
    }

    private static AuditEventData ToData(AuditEvent auditEvent) => new(
        auditEvent.EventId,
        auditEvent.SequenceNumber,
        auditEvent.EventType,
        auditEvent.ActorId,
        auditEvent.ResourceType,
        auditEvent.ResourceId,
        auditEvent.Payload,
        auditEvent.Timestamp,
        auditEvent.PreviousHash);
}
