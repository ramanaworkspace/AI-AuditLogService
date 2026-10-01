using System.Text.Json;
using AuditLogService.Domain;
using AuditLogService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditLogService.IntegrationTests.Persistence;

public sealed class AuditEventPersistenceTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture = fixture;

    [Fact]
    public async Task ValidEventPersistsAndCanBeRetrieved()
    {
        await _fixture.ResetAsync();
        var expected = CreateEvent(sequenceNumber: 1, eventId: EventId(1), previousHash: HashChain.GenesisHash);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            dbContext.AuditEvents.Add(AuditEventRecord.FromDomain(expected));
            await dbContext.SaveChangesAsync();
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var persisted = await verificationContext.AuditEvents.SingleAsync();
        var actual = persisted.ToDomain();

        Assert.Equal(expected.EventId, actual.EventId);
        Assert.Equal(expected.SequenceNumber, actual.SequenceNumber);
        Assert.Equal(expected.EventType, actual.EventType);
        Assert.Equal(expected.ActorId, actual.ActorId);
        Assert.Equal(expected.ResourceType, actual.ResourceType);
        Assert.Equal(expected.ResourceId, actual.ResourceId);
        Assert.Equal(expected.Timestamp, actual.Timestamp);
        Assert.Equal(expected.PreviousHash, actual.PreviousHash);
        Assert.Equal(expected.ContentHash, actual.ContentHash);
        Assert.True(JsonElement.DeepEquals(expected.Payload, actual.Payload));
    }

    [Fact]
    public async Task DuplicateEventIdIsRejectedByDatabase()
    {
        await _fixture.ResetAsync();
        var eventId = EventId(1);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            dbContext.AuditEvents.Add(AuditEventRecord.FromDomain(
                CreateEvent(sequenceNumber: 1, eventId: eventId, previousHash: HashChain.GenesisHash)));
            await dbContext.SaveChangesAsync();
        }

        await using var duplicateContext = _fixture.CreateDbContext();
        duplicateContext.AuditEvents.Add(AuditEventRecord.FromDomain(
            CreateEvent(sequenceNumber: 2, eventId: eventId, previousHash: new string('a', 64))));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateSequenceNumberIsRejectedByDatabase()
    {
        await _fixture.ResetAsync();
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.AuditEvents.Add(AuditEventRecord.FromDomain(
            CreateEvent(sequenceNumber: 1, eventId: EventId(1), previousHash: HashChain.GenesisHash)));
        await dbContext.SaveChangesAsync();

        dbContext.AuditEvents.Add(AuditEventRecord.FromDomain(
            CreateEvent(sequenceNumber: 1, eventId: EventId(2), previousHash: HashChain.GenesisHash)));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task EventsCanBeRetrievedInSequenceOrder()
    {
        await _fixture.ResetAsync();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            foreach (var sequenceNumber in new long[] { 3, 1, 2 })
            {
                var previousHash = sequenceNumber == 1 ? HashChain.GenesisHash : new string('a', 64);
                dbContext.AuditEvents.Add(AuditEventRecord.FromDomain(
                    CreateEvent(sequenceNumber, EventId((int)sequenceNumber), previousHash)));
            }

            await dbContext.SaveChangesAsync();
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var sequenceNumbers = await verificationContext.AuditEvents
            .OrderBy(record => record.SequenceNumber)
            .Select(record => record.SequenceNumber)
            .ToListAsync();

        Assert.Equal([1, 2, 3], sequenceNumbers);
    }

    [Fact]
    public async Task PersistedEventsCannotBeUpdatedOrDeleted()
    {
        await _fixture.ResetAsync();
        var auditEvent = CreateEvent(sequenceNumber: 1, eventId: EventId(1), previousHash: HashChain.GenesisHash);

        await using (var seedContext = _fixture.CreateDbContext())
        {
            seedContext.AuditEvents.Add(AuditEventRecord.FromDomain(auditEvent));
            await seedContext.SaveChangesAsync();
        }

        await using (var updateContext = _fixture.CreateDbContext())
        {
            var record = await updateContext.AuditEvents.SingleAsync();
            record.ActorId = "mutated-actor";

            await AssertAppendOnlyViolationAsync(updateContext.SaveChangesAsync());
        }

        await using var deleteContext = _fixture.CreateDbContext();
        var recordToDelete = await deleteContext.AuditEvents.SingleAsync();
        deleteContext.AuditEvents.Remove(recordToDelete);

        await AssertAppendOnlyViolationAsync(deleteContext.SaveChangesAsync());
    }

    private static AuditEvent CreateEvent(long sequenceNumber, Guid eventId, string previousHash)
    {
        using var payloadDocument = JsonDocument.Parse("""{"test":true,"sequence":1}""");
        var data = new AuditEventData(
            eventId,
            sequenceNumber,
            "RECORD_UPDATED",
            "integration-test-actor",
            "DOCUMENT",
            $"document-{sequenceNumber}",
            payloadDocument.RootElement,
            new DateTimeOffset(2026, 10, 1, 10, 0, (int)sequenceNumber, TimeSpan.Zero),
            previousHash);
        var serializer = new CanonicalEventSerializer();
        return AuditEvent.Create(data, new Sha256EventHasher(serializer));
    }

    private static Guid EventId(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

    private static async Task AssertAppendOnlyViolationAsync(Task saveChanges)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => saveChanges);
        var updateException = Assert.IsType<DbUpdateException>(exception.InnerException);
        var postgresException = Assert.IsType<PostgresException>(updateException.InnerException);
        Assert.Equal("55000", postgresException.SqlState);
    }
}
