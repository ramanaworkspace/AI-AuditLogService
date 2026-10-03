using System.Net.Http.Json;
using AuditLogService.Api.Contracts;
using AuditLogService.Application.Verification;
using AuditLogService.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditLogService.IntegrationTests.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class AuditEventTamperingTests : IDisposable
{
    private readonly PostgreSqlFixture _fixture;
    private readonly AuditEventApiFactory _factory;
    private readonly HttpClient _client;

    public AuditEventTamperingTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _factory = new AuditEventApiFactory { ConnectionString = fixture.ConnectionString };
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Theory]
    [InlineData("payload", 1, ChainViolationType.ContentHashMismatch, 2)]
    [InlineData("actor", 1, ChainViolationType.ContentHashMismatch, 2)]
    [InlineData("contentHash", 1, ChainViolationType.ContentHashMismatch, 2)]
    [InlineData("previousHash", 1, ChainViolationType.PreviousHashMismatch, 2)]
    [InlineData("wrongPredecessor", 2, ChainViolationType.PreviousHashMismatch, 3)]
    [InlineData("genesis", 0, ChainViolationType.InvalidGenesisRelationship, 1)]
    [InlineData("sequence", 2, ChainViolationType.SequenceGap, 5)]
    public async Task DirectStoreModificationReportsFirstInconsistency(
        string mutation,
        int eventIndex,
        ChainViolationType expectedViolation,
        long expectedSequence)
    {
        var events = await WriteAndVerifyValidChainAsync();
        var target = events[eventIndex];
        var alternateHash = new string('f', 64);
        Assert.NotEqual(alternateHash, target.ContentHash);

        FormattableString sql = mutation switch
        {
            "payload" => $"UPDATE audit_events SET payload = '{{\"title\":\"changed\"}}'::jsonb WHERE event_id = {target.EventId}",
            "actor" => $"UPDATE audit_events SET actor_id = 'changed-actor' WHERE event_id = {target.EventId}",
            "contentHash" => $"UPDATE audit_events SET content_hash = {alternateHash} WHERE event_id = {target.EventId}",
            "previousHash" => $"UPDATE audit_events SET previous_hash = {alternateHash} WHERE event_id = {target.EventId}",
            "wrongPredecessor" => $"UPDATE audit_events SET previous_hash = {events[0].ContentHash} WHERE event_id = {target.EventId}",
            "genesis" => $"UPDATE audit_events SET previous_hash = {alternateHash} WHERE event_id = {target.EventId}",
            "sequence" => $"UPDATE audit_events SET sequence_number = 5 WHERE event_id = {target.EventId}",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        await MutateStoreAsync(sql);

        var result = await VerifyAsync();
        Assert.False(result.IsValid);
        Assert.Equal(eventIndex, result.EventsVerified);
        Assert.Equal(target.EventId, result.FirstInconsistentEventId);
        Assert.Equal(expectedSequence, result.FirstInconsistentSequenceNumber);
        Assert.Equal(expectedViolation, result.ViolationType);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DeletedRecordIsReportedAsMissingIncludingTheTail(int eventIndex)
    {
        var events = await WriteAndVerifyValidChainAsync();
        await MutateStoreAsync($"DELETE FROM audit_events WHERE event_id = {events[eventIndex].EventId}");

        var result = await VerifyAsync();
        Assert.False(result.IsValid);
        Assert.Equal(eventIndex, result.EventsVerified);
        Assert.Null(result.FirstInconsistentEventId);
        Assert.Equal(events[eventIndex].SequenceNumber, result.FirstInconsistentSequenceNumber);
        Assert.Equal(ChainViolationType.MissingRecord, result.ViolationType);
    }

    [Fact]
    public async Task DeletedEntireChainDoesNotVerifyAsAnEmptyValidChain()
    {
        await WriteAndVerifyValidChainAsync();
        await MutateStoreAsync($"DELETE FROM audit_events");

        var result = await VerifyAsync();
        Assert.False(result.IsValid);
        Assert.Equal(0, result.EventsVerified);
        Assert.Null(result.FirstInconsistentEventId);
        Assert.Equal(1, result.FirstInconsistentSequenceNumber);
        Assert.Equal(ChainViolationType.MissingRecord, result.ViolationType);
    }

    [Fact]
    public async Task MultipleTamperedRecordsReportTheEarliestNotTheLastMutation()
    {
        var events = await WriteAndVerifyValidChainAsync();
        await MutateStoreAsync($"UPDATE audit_events SET actor_id = 'later-tampering' WHERE event_id = {events[2].EventId}");
        await MutateStoreAsync($"UPDATE audit_events SET payload = '{{\"changed\":true}}'::jsonb WHERE event_id = {events[0].EventId}");

        var result = await VerifyAsync();
        Assert.False(result.IsValid);
        Assert.Equal(0, result.EventsVerified);
        Assert.Equal(events[0].EventId, result.FirstInconsistentEventId);
        Assert.Equal(1, result.FirstInconsistentSequenceNumber);
        Assert.Equal(ChainViolationType.ContentHashMismatch, result.ViolationType);
    }

    [Fact]
    public async Task AlteredHeadHashIsNotReportedAsAValidChain()
    {
        var events = await WriteAndVerifyValidChainAsync();
        var alternateHash = new string('f', 64);
        await MutateStoreAsync($"UPDATE chain_metadata SET head_hash = {alternateHash} WHERE chain_id = 1");

        var result = await VerifyAsync();
        Assert.False(result.IsValid);
        Assert.Equal(3, result.EventsVerified);
        Assert.Equal(events[2].EventId, result.FirstInconsistentEventId);
        Assert.Equal(3, result.FirstInconsistentSequenceNumber);
        Assert.Equal(ChainViolationType.ChainHeadMismatch, result.ViolationType);
    }

    [Fact]
    public async Task ContentTamperingBeforeDeletionIsReportedFirst()
    {
        var events = await WriteAndVerifyValidChainAsync();
        await MutateStoreAsync($"DELETE FROM audit_events WHERE event_id = {events[1].EventId}");
        await MutateStoreAsync($"UPDATE audit_events SET actor_id = 'changed-actor' WHERE event_id = {events[0].EventId}");

        var result = await VerifyAsync();
        Assert.False(result.IsValid);
        Assert.Equal(0, result.EventsVerified);
        Assert.Equal(events[0].EventId, result.FirstInconsistentEventId);
        Assert.Equal(1, result.FirstInconsistentSequenceNumber);
        Assert.Equal(ChainViolationType.ContentHashMismatch, result.ViolationType);
    }

    private async Task<List<AuditEventResponse>> WriteAndVerifyValidChainAsync()
    {
        await _fixture.ResetAsync();
        var events = new List<AuditEventResponse>();
        for (var index = 0; index < 3; index++)
        {
            using var response = await _client.PostAsJsonAsync("/api/v1/audit-events", new
            {
                eventType = "document.created",
                actorId = "actor-1",
                resourceType = "document",
                resourceId = $"doc-{index}",
                payload = new { title = "Original" }
            });
            Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<AuditEventResponse>();
            Assert.NotNull(created);
            events.Add(created);
        }

        var result = await VerifyAsync();
        Assert.True(result.IsValid);
        Assert.Equal(3, result.EventsVerified);
        Assert.Null(result.FirstInconsistentEventId);
        Assert.Null(result.FirstInconsistentSequenceNumber);
        Assert.Null(result.ViolationType);
        return events;
    }

    private async Task<ChainVerificationResponse> VerifyAsync()
    {
        using var response = await _client.GetAsync("/api/v1/audit-events/verify");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ChainVerificationResponse>();
        Assert.NotNull(result);
        return result;
    }

    private async Task MutateStoreAsync(FormattableString sql)
    {
        await using var dbContext = _fixture.CreateDbContext();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        // Test-only privileged bypass; SET LOCAL is restored on commit or rollback.
        await dbContext.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
        Assert.True(await dbContext.Database.ExecuteSqlInterpolatedAsync(sql) > 0);
        await transaction.CommitAsync();
    }
}
