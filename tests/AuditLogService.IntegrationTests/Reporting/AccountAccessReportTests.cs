using System.Net;
using System.Text.Json;
using AuditLogService.Application.Reporting;
using AuditLogService.Application.Append;
using AuditLogService.Application.Redaction;
using AuditLogService.Domain;
using AuditLogService.Infrastructure.Persistence;
using AuditLogService.Infrastructure.Reporting;
using AuditLogService.IntegrationTests.Api;
using AuditLogService.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditLogService.IntegrationTests.Reporting;

[Collection(PostgreSqlCollection.Name)]
public sealed class AccountAccessReportTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedOutcomes = ["succeeded", "denied", "failed"];
    private readonly PostgreSqlFixture _fixture = fixture;

    [Fact]
    public async Task ReportIncludesExactBoundariesAndArchivedAccessExcludesUnrelatedAndIsDeterministic()
    {
        var events = await SeedAsync(
            (AccountAccessPolicy.Succeeded, "CLIENT_ACCOUNT", Start.AddTicks(-10)),
            (AccountAccessPolicy.Succeeded, "CLIENT_ACCOUNT", Start.AddHours(1)),
            ("CLIENT_ACCOUNT_UPDATED", "CLIENT_ACCOUNT", Start),
            (AccountAccessPolicy.Denied, "DOCUMENT", Start),
            (AccountAccessPolicy.Denied, "CLIENT_ACCOUNT", Start),
            (AccountAccessPolicy.Failed, "CLIENT_ACCOUNT", Start.AddHours(2)),
            (AccountAccessPolicy.Succeeded, "CLIENT_ACCOUNT", Start.AddHours(2).AddTicks(10)));
        await using (var context = _fixture.CreateDbContext())
        {
            context.AuditEventArchives.Add(new AuditEventArchive
            { EventId = events[4].EventId, ArchivedAt = Start.AddDays(1).UtcDateTime });
            await context.SaveChangesAsync();
        }

        using var factory = Factory();
        using var client = factory.CreateClient();
        var url = Url(Start, Start.AddHours(2));
        var first = await client.GetByteArrayAsync(url);
        Assert.Equal(first, await client.GetByteArrayAsync(url));
        using var report = JsonDocument.Parse(first);
        var rows = report.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new long[] { 2, 5, 6 }, rows.Select(row => row.GetProperty("sequenceNumber").GetInt64()));
        Assert.Equal(ExpectedOutcomes, rows.Select(row => row.GetProperty("outcome").GetString()));
        Assert.True(rows[1].GetProperty("isArchived").GetBoolean());
        Assert.Equal("2026-10-04T00:00:00.0000000Z", rows[1].GetProperty("archivedAt").GetString());
        Assert.All(rows, row =>
        {
            Assert.Equal("[REDACTED]", row.GetProperty("payload").GetProperty("accountNumber").GetString());
            Assert.Equal("readable", row.GetProperty("payload").GetProperty("description").GetString());
        });
        var chain = report.RootElement.GetProperty("chainStatus");
        Assert.True(chain.GetProperty("isValid").GetBoolean());
        Assert.Equal(7, chain.GetProperty("headSequenceNumber").GetInt64());
        Assert.Equal(events[^1].ContentHash, chain.GetProperty("headHash").GetString());
        Assert.Equal(7, chain.GetProperty("eventsVerified").GetInt64());
        Assert.Equal(1, chain.GetProperty("archivedEventsVerified").GetInt64());
        Assert.Equal(first, await client.GetByteArrayAsync(Url(Start.ToOffset(TimeSpan.FromHours(5.5)),
            Start.AddHours(2).ToOffset(TimeSpan.FromHours(5.5)))));
        using var equalBoundary = JsonDocument.Parse(await client.GetByteArrayAsync(Url(Start, Start)));
        Assert.Single(equalBoundary.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task EmptyReportStillContainsGlobalChainStatus()
    {
        await _fixture.ResetAsync();
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var openApi = JsonDocument.Parse(await client.GetByteArrayAsync("/openapi/v1.json"));
        Assert.True(openApi.RootElement.GetProperty("paths").TryGetProperty("/api/v1/reports/account-access", out _));
        using var report = JsonDocument.Parse(await client.GetByteArrayAsync(Url(Start, Start)));
        Assert.Empty(report.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(report.RootElement.GetProperty("chainStatus").GetProperty("isValid").GetBoolean());
        Assert.Equal("GENESIS", report.RootElement.GetProperty("chainStatus").GetProperty("headHash").GetString());
        await SeedAsync(("unrelated", "DOCUMENT", Start));
        using var withUnrelated = JsonDocument.Parse(await client.GetByteArrayAsync(Url(Start, Start)));
        Assert.Empty(withUnrelated.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(1, withUnrelated.RootElement.GetProperty("chainStatus").GetProperty("eventsVerified").GetInt64());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?startTime=2026-10-03T00:00:00Z")]
    [InlineData("?startTime=bad&endTime=bad")]
    [InlineData("?startTime=2026-10-04T00:00:00Z&endTime=2026-10-03T00:00:00Z")]
    [InlineData("?startTime=2026-10-03T00:00:00&endTime=2026-10-03T01:00:00Z")]
    [InlineData("?startTime=2026-10-03T00:00:00Z&endTime=2026-10-03T01:00:00Z&resourceType=DOCUMENT")]
    public async Task InvalidParametersReturnValidationProblem(string query)
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/reports/account-access" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.True(problem.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task TamperingOutsideSelectedRowsIsExplicitlyReported()
    {
        var events = await SeedAsync(("unrelated", "DOCUMENT", Start),
            (AccountAccessPolicy.Succeeded, "CLIENT_ACCOUNT", Start));
        await using (var context = _fixture.CreateDbContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE audit_events SET content_hash = {new string('f', 64)} WHERE event_id = {events[0].EventId}");
            await transaction.CommitAsync();
        }

        using var factory = Factory();
        using var client = factory.CreateClient();
        using var report = JsonDocument.Parse(await client.GetByteArrayAsync(Url(Start, Start)));
        Assert.Single(report.RootElement.GetProperty("items").EnumerateArray());
        var chain = report.RootElement.GetProperty("chainStatus");
        Assert.False(chain.GetProperty("isValid").GetBoolean());
        Assert.Equal("ContentHashMismatch", chain.GetProperty("violationType").GetString());
        Assert.Equal(events[0].EventId, chain.GetProperty("firstInconsistentEventId").GetGuid());
        Assert.Equal(1, chain.GetProperty("firstInconsistentSequenceNumber").GetInt64());
    }

    [Fact]
    public async Task MissingRecordReturnsMissingClassificationAndNoFabricatedIdentifier()
    {
        var events = await SeedAsync((AccountAccessPolicy.Succeeded, "CLIENT_ACCOUNT", Start));
        await using (var context = _fixture.CreateDbContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM audit_events WHERE event_id = {events[0].EventId}");
            await transaction.CommitAsync();
        }

        using var factory = Factory();
        using var client = factory.CreateClient();
        using var report = JsonDocument.Parse(await client.GetByteArrayAsync(Url(Start, Start)));
        Assert.Empty(report.RootElement.GetProperty("items").EnumerateArray());
        var chain = report.RootElement.GetProperty("chainStatus");
        Assert.False(chain.GetProperty("isValid").GetBoolean());
        Assert.Equal("MissingRecord", chain.GetProperty("violationType").GetString());
        Assert.Equal(JsonValueKind.Null, chain.GetProperty("firstInconsistentEventId").ValueKind);
        Assert.Equal(1, chain.GetProperty("firstInconsistentSequenceNumber").GetInt64());
        Assert.Equal(1, chain.GetProperty("headSequenceNumber").GetInt64());
    }

    private AuditEventApiFactory Factory() => new() { ConnectionString = _fixture.ConnectionString };

    [Fact]
    public async Task ConcurrentAppendDoesNotChangeReportsSnapshotBoundary()
    {
        var events = await SeedAsync((AccountAccessPolicy.Succeeded, "CLIENT_ACCOUNT", Start));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var service = new PostgresAccountAccessReportService(new ReportContextFactory(_fixture),
            new Sha256EventHasher(new CanonicalEventSerializer()), new PausingProtector(entered, release));
        var pending = Task.Run(() => service.CreateAsync(new AccountAccessReportQuery(Start, Start.AddYears(1))));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await _fixture.CreateAppendService().AppendAsync(new AppendAuditEventRequest(
                AccountAccessPolicy.Succeeded, "actor", "CLIENT_ACCOUNT", "account-2",
                JsonSerializer.Deserialize<JsonElement>("{}")));
        }
        finally
        {
            release.Set();
        }

        var report = await pending;
        Assert.Single(report.Items);
        Assert.True(report.ChainStatus.IsValid);
        Assert.Equal(1, report.ChainStatus.HeadSequenceNumber);
        Assert.Equal(1, report.ChainStatus.EventsVerified);
        Assert.Equal(events[0].ContentHash, report.ChainStatus.HeadHash);
        await using var context = _fixture.CreateDbContext();
        Assert.Equal(2, (await context.ChainMetadata.SingleAsync()).HeadSequenceNumber);
    }

    private sealed class ReportContextFactory(PostgreSqlFixture fixture) : IDbContextFactory<AuditLogDbContext>
    {
        public AuditLogDbContext CreateDbContext() => fixture.CreateDbContext();

        public Task<AuditLogDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class PausingProtector(TaskCompletionSource entered, ManualResetEventSlim release) : IPayloadProtector
    {
        private readonly CommitmentPayloadProtector _inner = new(["/accountNumber"]);

        public JsonElement Protect(JsonElement payload) => _inner.Protect(payload);

        public JsonElement Project(JsonElement payload)
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15)))
            {
                throw new TimeoutException("Concurrent snapshot test did not release projection.");
            }

            return _inner.Project(payload);
        }
    }

    private static string Url(DateTimeOffset start, DateTimeOffset end) =>
        "/api/v1/reports/account-access?startTime=" + Uri.EscapeDataString(start.ToString("O"))
        + "&endTime=" + Uri.EscapeDataString(end.ToString("O"));

    private async Task<List<AuditEvent>> SeedAsync(params (string Type, string Resource, DateTimeOffset Time)[] inputs)
    {
        await _fixture.ResetAsync();
        var hasher = new Sha256EventHasher(new CanonicalEventSerializer());
        var events = new List<AuditEvent>();
        var previous = HashChain.GenesisHash;
        await using var context = _fixture.CreateDbContext();
        foreach (var input in inputs)
        {
            var item = AuditEvent.Create(new AuditEventData(Guid.NewGuid(), events.Count + 1,
                input.Type, "actor", input.Resource, "account-1",
                JsonSerializer.Deserialize<JsonElement>("""{"accountNumber":"PRIVATE","description":"readable"}"""),
                input.Time, previous), hasher);
            context.AuditEvents.Add(AuditEventRecord.FromDomain(item));
            events.Add(item);
            previous = item.ContentHash;
        }

        var head = await context.ChainMetadata.SingleAsync();
        head.HeadSequenceNumber = events.Count;
        head.HeadHash = previous;
        await context.SaveChangesAsync();
        return events;
    }
}
