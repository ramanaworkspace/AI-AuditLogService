using System.Net.Http.Json;
using System.Text.Json;
using AuditLogService.Api.Contracts;
using AuditLogService.Application.Retention;
using AuditLogService.Application.Verification;
using AuditLogService.Domain;
using AuditLogService.Infrastructure;
using AuditLogService.Infrastructure.Persistence;
using AuditLogService.Infrastructure.Retention;
using AuditLogService.IntegrationTests.Api;
using AuditLogService.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AuditLogService.IntegrationTests.Retention;

[Collection(PostgreSqlCollection.Name)]
public sealed class AuditEventRetentionTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromDays(30);
    private readonly PostgreSqlFixture _fixture = fixture;

    [Fact]
    public async Task EligibilityUsesStrictBoundaryAndConfiguredWindow()
    {
        var events = await SeedAsync(Now - Window - TimeSpan.FromMicroseconds(1), Now - Window, Now);
        var service = CreateService();

        Assert.True((await service.GetStateAsync(events[0].EventId)).IsEligible);
        Assert.False((await service.GetStateAsync(events[1].EventId)).IsEligible);
        Assert.False((await service.GetStateAsync(events[2].EventId)).IsEligible);
        Assert.False((await service.GetStateAsync(events[0].EventId)).IsArchived);
        var longerWindow = _fixture.CreateRetentionService(TimeSpan.FromDays(31), new FixedTimeProvider(Now));
        Assert.False((await longerWindow.GetStateAsync(events[0].EventId)).IsEligible);

        var result = await service.ArchiveEligibleAsync();
        Assert.Equal(Now - Window, result.EligibilityCutoff);
        Assert.Equal(Now, result.ArchivedAt);
        Assert.Equal(1, result.RecordsArchived);
    }

    [Fact]
    public async Task ArchivalPreservesEveryHashedFieldAndHeadAndRemainsQueryable()
    {
        var events = await SeedAsync(Now.AddDays(-40), Now.AddDays(-31), Now);
        using var factory = new AuditEventApiFactory { ConnectionString = _fixture.ConnectionString };
        using var client = factory.CreateClient();
        var before = await client.GetFromJsonAsync<ChainVerificationResponse>("/api/v1/audit-events/verify");
        Assert.NotNull(before);
        Assert.True(before.IsValid);
        Assert.Equal(0, before.ArchivedEventsVerified);

        var result = await CreateService().ArchiveEligibleAsync();
        Assert.Equal(2, result.RecordsArchived);

        await using var dbContext = _fixture.CreateDbContext();
        var persisted = await dbContext.AuditEvents.OrderBy(record => record.SequenceNumber).ToListAsync();
        Assert.Equal(events.Count, persisted.Count);
        var serializer = new CanonicalEventSerializer();
        for (var index = 0; index < events.Count; index++)
        {
            Assert.Equal(serializer.Serialize(events[index].Data), serializer.Serialize(persisted[index].ToDomain().Data));
            Assert.Equal(events[index].ContentHash, persisted[index].ContentHash);
        }

        var head = await dbContext.ChainMetadata.SingleAsync();
        Assert.Equal(events[^1].SequenceNumber, head.HeadSequenceNumber);
        Assert.Equal(events[^1].ContentHash, head.HeadHash);

        var verification = await client.GetFromJsonAsync<ChainVerificationResponse>("/api/v1/audit-events/verify");
        Assert.NotNull(verification);
        Assert.True(verification.IsValid);
        Assert.Equal(3, verification.EventsVerified);
        Assert.Equal(2, verification.ArchivedEventsVerified);
        Assert.Null(verification.ViolationType);

        var page = await client.GetFromJsonAsync<AuditEventListResponse>("/api/v1/audit-events");
        Assert.NotNull(page);
        Assert.Equal(3, page.Items.Count);
        Assert.All(page.Items.Take(2), item =>
        {
            Assert.True(item.IsArchived);
            Assert.Equal(Now, item.ArchivedAt);
        });
        Assert.False(page.Items[2].IsArchived);
        Assert.Null(page.Items[2].ArchivedAt);
    }

    [Fact]
    public async Task RepeatedAndConcurrentArchivalIsIdempotent()
    {
        var events = await SeedAsync(Now.AddDays(-40), Now.AddDays(-35));
        var results = await Task.WhenAll(
            CreateService().ArchiveEligibleAsync(), CreateService().ArchiveEligibleAsync());
        Assert.Equal(2, results.Sum(result => result.RecordsArchived));

        var laterService = _fixture.CreateRetentionService(Window, new FixedTimeProvider(Now.AddDays(1)));
        Assert.Equal(0, (await laterService.ArchiveEligibleAsync()).RecordsArchived);
        foreach (var auditEvent in events)
        {
            var state = await laterService.GetStateAsync(auditEvent.EventId);
            Assert.True(state.IsArchived);
            Assert.Equal(Now, state.ArchivedAt);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArchiveMarkerDoesNotHideTamperingOrMissingRecord(bool deleteRecord)
    {
        var events = await SeedAsync(Now.AddDays(-40), Now.AddDays(-35), Now);
        await CreateService().ArchiveEligibleAsync();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            await dbContext.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
            FormattableString mutation;
            if (deleteRecord)
            {
                mutation = $"DELETE FROM audit_events WHERE event_id = {events[1].EventId}";
            }
            else
            {
                mutation = $"UPDATE audit_events SET actor_id = 'tampered-actor' WHERE event_id = {events[1].EventId}";
            }
            Assert.Equal(1, await dbContext.Database.ExecuteSqlInterpolatedAsync(mutation));
            await transaction.CommitAsync();
        }

        using var factory = new AuditEventApiFactory { ConnectionString = _fixture.ConnectionString };
        using var client = factory.CreateClient();
        var result = await client.GetFromJsonAsync<ChainVerificationResponse>("/api/v1/audit-events/verify");
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Equal(1, result.EventsVerified);
        Assert.Equal(1, result.ArchivedEventsVerified);
        Assert.Equal(2, result.FirstInconsistentSequenceNumber);
        Assert.Equal(deleteRecord ? ChainViolationType.MissingRecord : ChainViolationType.ContentHashMismatch,
            result.ViolationType);
        Assert.Equal(deleteRecord ? (Guid?)null : events[1].EventId, result.FirstInconsistentEventId);
        if (deleteRecord)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => CreateService().GetStateAsync(events[1].EventId));
        }
    }

    [Fact]
    public async Task NoEligibleEventsAndEmptyChainAreNoOps()
    {
        await SeedAsync();
        Assert.Equal(0, (await CreateService().ArchiveEligibleAsync()).RecordsArchived);
        await SeedAsync(Now - Window, Now.AddDays(1));
        Assert.Equal(0, (await CreateService().ArchiveEligibleAsync()).RecordsArchived);
    }

    [Fact]
    public async Task AdvancingTimeMakesBoundaryEventEligible()
    {
        var events = await SeedAsync(Now - Window);
        Assert.Equal(0, (await CreateService().ArchiveEligibleAsync()).RecordsArchived);
        var later = _fixture.CreateRetentionService(Window, new FixedTimeProvider(Now.AddSeconds(1)));
        Assert.Equal(1, (await later.ArchiveEligibleAsync()).RecordsArchived);
        Assert.True((await later.GetStateAsync(events[0].EventId)).IsArchived);
    }

    [Fact]
    public async Task SubMicrosecondWindowUsesTheSameEligibilityInMemoryAndPostgreSql()
    {
        var events = await SeedAsync(Now - Window);
        var service = _fixture.CreateRetentionService(Window - TimeSpan.FromTicks(1), new FixedTimeProvider(Now));
        Assert.True((await service.GetStateAsync(events[0].EventId)).IsEligible);
        Assert.Equal(1, (await service.ArchiveEligibleAsync()).RecordsArchived);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidWindowsFailWithoutArchiving(int days)
    {
        await SeedAsync(Now.AddDays(-40));
        var service = _fixture.CreateRetentionService(TimeSpan.FromDays(days), new FixedTimeProvider(Now));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ArchiveEligibleAsync());
        await using var dbContext = _fixture.CreateDbContext();
        Assert.False(await dbContext.AuditEventArchives.AnyAsync());
    }

    [Fact]
    public async Task CancellationDoesNotCreateArchiveState()
    {
        await SeedAsync(Now.AddDays(-40));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService().ArchiveEligibleAsync(cancellation.Token));
        await using var dbContext = _fixture.CreateDbContext();
        Assert.False(await dbContext.AuditEventArchives.AnyAsync());
    }

    [Fact]
    public async Task UnknownEventIsNotRepresentedAsActiveOrArchived()
    {
        await SeedAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService().GetStateAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("45.00:00:00", true)]
    [InlineData("00:00:00", false)]
    public void RegistrationReadsAndValidatesRetentionConfiguration(string window, bool valid)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Retention:Window"] = window
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAuditLogInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        if (valid)
        {
            Assert.Equal(TimeSpan.FromDays(45), provider.GetRequiredService<IOptions<RetentionOptions>>().Value.Window);
        }
        else
        {
            Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<RetentionOptions>>().Value);
        }
    }

    private IAuditEventRetentionService CreateService() =>
        _fixture.CreateRetentionService(Window, new FixedTimeProvider(Now));

    private async Task<List<AuditEvent>> SeedAsync(params DateTimeOffset[] timestamps)
    {
        await _fixture.ResetAsync();
        using var payload = JsonDocument.Parse("""{"original":true,"nested":{"value":null}}""");
        var hasher = new Sha256EventHasher(new CanonicalEventSerializer());
        var events = new List<AuditEvent>();
        var previousHash = HashChain.GenesisHash;
        await using var dbContext = _fixture.CreateDbContext();
        for (var index = 0; index < timestamps.Length; index++)
        {
            var auditEvent = AuditEvent.Create(new AuditEventData(
                Guid.NewGuid(), index + 1, "document.created", "actor-1", "document",
                $"doc-{index}", payload.RootElement, timestamps[index], previousHash), hasher);
            events.Add(auditEvent);
            dbContext.AuditEvents.Add(AuditEventRecord.FromDomain(auditEvent));
            previousHash = auditEvent.ContentHash;
        }

        var head = await dbContext.ChainMetadata.SingleAsync();
        head.HeadSequenceNumber = events.Count;
        head.HeadHash = previousHash;
        await dbContext.SaveChangesAsync();
        return events;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
