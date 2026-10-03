using System.Net.Http.Json;
using System.Text.Json;
using AuditLogService.Api.Contracts;
using AuditLogService.Application.Append;
using AuditLogService.Application.Redaction;
using AuditLogService.Domain;
using AuditLogService.Infrastructure.Append;
using AuditLogService.Infrastructure.Persistence;
using AuditLogService.Infrastructure.Verification;
using AuditLogService.IntegrationTests.Api;
using AuditLogService.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditLogService.IntegrationTests.Concurrency;

[Collection(PostgreSqlCollection.Name)]
public sealed class ScenarioDCorrectnessTests(PostgreSqlFixture fixture)
{
    private readonly PostgreSqlFixture _fixture = fixture;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task FiftyContendingWritersPersistEveryAcknowledgedWriteInOneChain(int repetition)
    {
        await _fixture.ResetAsync();
        const int writers = 50;
        const int writesPerWriter = 3;
        var applicationName = $"scenario-d-{Guid.NewGuid():N}";
        var settings = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        { ApplicationName = applicationName, MaxPoolSize = 60, CommandTimeout = 180 };
        var factory = new ContextFactory(settings.ConnectionString);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var blocker = _fixture.CreateDbContext();
        await using var lockTransaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({ChainAdvisoryLock.Key})");
        var tasks = Enumerable.Range(0, writers).Select(async writer =>
        {
            await start.Task;
            var service = Service(factory);
            var receipts = new List<AuditEvent>();
            for (var write = 0; write < writesPerWriter; write++)
            {
                receipts.Add(await service.AppendAsync(Request($"run-{repetition}-writer-{writer}-write-{write}")));
            }

            return receipts;
        }).ToArray();
        start.SetResult();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using var monitor = _fixture.CreateDbContext();
            var waiting = 0;
            while (waiting < writers)
            {
                waiting = await monitor.Database.SqlQuery<int>(
                    $"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE application_name = {applicationName} AND wait_event = 'advisory'")
                    .SingleAsync(deadline.Token);
                if (waiting < writers)
                {
                    await Task.Delay(25, deadline.Token);
                }
            }

            Assert.Equal(writers, waiting);
            Assert.All(tasks, task => Assert.False(task.IsCompleted));
        }
        finally
        {
            await lockTransaction.RollbackAsync();
            try
            {
                await Task.WhenAll(tasks);
            }
            finally
            {
                using var poolConnection = new NpgsqlConnection(settings.ConnectionString);
                NpgsqlConnection.ClearPool(poolConnection);
            }
        }

        var committed = tasks.SelectMany(task => task.Result).ToArray();
        Assert.Equal(writers * writesPerWriter, committed.Length);
        await AssertStateAsync(committed);
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("head")]
    public async Task PostgreSqlStatementFailureRollsBackAndExplicitRetryContinuesFromPriorHead(string stage)
    {
        await _fixture.ResetAsync();
        var factory = new ContextFactory(_fixture.ConnectionString);
        var first = await Service(factory).AppendAsync(Request("before"));
        var request = Request("retry-after-known-rollback");
        await using var setup = _fixture.CreateDbContext();
        await setup.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION scenario_d_reject_write() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Scenario D injected statement failure' USING ERRCODE = 'P0001'; END;
            $$;
            """);
        try
        {
            await setup.Database.ExecuteSqlRawAsync(
                stage == "insert"
                    ? "CREATE TRIGGER scenario_d_reject BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION scenario_d_reject_write()"
                    : "CREATE TRIGGER scenario_d_reject BEFORE UPDATE ON chain_metadata FOR EACH ROW EXECUTE FUNCTION scenario_d_reject_write()");
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => Service(factory).AppendAsync(request));
            Assert.Equal("P0001", Assert.IsType<PostgresException>(error.InnerException).SqlState);
            await AssertStateAsync([first]);
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync(stage == "insert"
                ? "DROP TRIGGER IF EXISTS scenario_d_reject ON audit_events"
                : "DROP TRIGGER IF EXISTS scenario_d_reject ON chain_metadata");
            await setup.Database.ExecuteSqlRawAsync("DROP FUNCTION scenario_d_reject_write()");
        }

        var retry = await Service(factory).AppendAsync(request);
        Assert.Equal(2, retry.SequenceNumber);
        Assert.Equal(first.ContentHash, retry.PreviousHash);
        await AssertStateAsync([first, retry]);
    }

    [Fact]
    public async Task FailureImmediatelyBeforeCommitRollsBackSavedEventProjectionAndHead()
    {
        await _fixture.ResetAsync();
        var normal = new ContextFactory(_fixture.ConnectionString);
        var first = await Service(normal).AppendAsync(Request("before"));
        var failing = new ContextFactory(_fixture.ConnectionString, new RejectCommit());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(failing).AppendAsync(Request("rolled-back")));
        await AssertStateAsync([first]);
        var recovered = await Service(normal).AppendAsync(Request("rolled-back"));
        await AssertStateAsync([first, recovered]);
    }

    [Fact]
    public async Task ConnectionFailureIsExplicitAndRetryOnHealthyDatabaseDoesNotLoseSequence()
    {
        await _fixture.ResetAsync();
        var normal = new ContextFactory(_fixture.ConnectionString);
        var first = await Service(normal).AppendAsync(Request("before"));
        var settings = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        { Database = $"scenario_d_absent_{Guid.NewGuid():N}", Pooling = false, Timeout = 5 };
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            Service(new ContextFactory(settings.ConnectionString)).AppendAsync(Request("connection-retry")));
        Assert.Equal("3D000", error.SqlState);
        await AssertStateAsync([first]);
        var retried = await Service(normal).AppendAsync(Request("connection-retry"));
        await AssertStateAsync([first, retried]);
    }

    [Fact]
    public async Task RepeatingAnAlreadyCommittedRequestAppendsAgainRatherThanClaimingIdempotency()
    {
        await _fixture.ResetAsync();
        var service = Service(new ContextFactory(_fixture.ConnectionString));
        var request = Request("same-logical-request");
        var first = await service.AppendAsync(request);
        var repeated = await service.AppendAsync(request);
        Assert.NotEqual(first.EventId, repeated.EventId);
        Assert.Equal(first.SequenceNumber + 1, repeated.SequenceNumber);
        await AssertStateAsync([first, repeated]);
    }

    [Fact]
    public async Task RecreatedApplicationHostRecoversCommittedHeadAndContinuesChain()
    {
        await _fixture.ResetAsync();
        AuditEventResponse first;
        using (var host = new AuditEventApiFactory { ConnectionString = _fixture.ConnectionString })
        using (var client = host.CreateClient())
        using (var response = await client.PostAsJsonAsync("/api/v1/audit-events", new
        {
            eventType = "scenario-d",
            actorId = "before-restart",
            resourceType = "document",
            resourceId = "1",
            payload = new { token = "before-restart" }
        }))
        {
            response.EnsureSuccessStatusCode();
            first = (await response.Content.ReadFromJsonAsync<AuditEventResponse>())!;
            Assert.NotNull(first);
        }

        using var restarted = new AuditEventApiFactory { ConnectionString = _fixture.ConnectionString };
        using var restartedClient = restarted.CreateClient();
        using var nextResponse = await restartedClient.PostAsJsonAsync("/api/v1/audit-events", new
        {
            eventType = "scenario-d",
            actorId = "after-restart",
            resourceType = "document",
            resourceId = "1",
            payload = new { token = "after-restart" }
        });
        nextResponse.EnsureSuccessStatusCode();
        var next = await nextResponse.Content.ReadFromJsonAsync<AuditEventResponse>();
        Assert.NotNull(next);
        Assert.Equal(first.SequenceNumber + 1, next.SequenceNumber);
        Assert.Equal(first.ContentHash, next.PreviousHash);
        await using var context = _fixture.CreateDbContext();
        var persisted = await context.AuditEvents.OrderBy(row => row.SequenceNumber).ToListAsync();
        Assert.Equal(first.EventId, persisted[0].EventId);
        Assert.Equal(next.EventId, persisted[1].EventId);
        await AssertStateAsync(persisted.Select(row => row.ToDomain()).ToArray());
        var verification = await restartedClient.GetFromJsonAsync<ChainVerificationResponse>("/api/v1/audit-events/verify");
        Assert.NotNull(verification);
        Assert.True(verification.IsValid);
        Assert.Equal(2, verification.EventsVerified);
    }

    private async Task AssertStateAsync(AuditEvent[] receipts)
    {
        await using var context = _fixture.CreateDbContext();
        var stored = await context.AuditEvents.AsNoTracking().OrderBy(row => row.SequenceNumber).ToListAsync();
        Assert.Equal(receipts.Length, stored.Count);
        Assert.Equal(receipts.Select(item => item.EventId).Order(), stored.Select(item => item.EventId).Order());
        Assert.Equal(receipts.Length, stored.Select(item => item.EventId).Distinct().Count());
        Assert.Equal(receipts.Length, stored.Select(item => item.PreviousHash).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(receipts.Length, stored.Select(item => item.ContentHash).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Range(1, receipts.Length).Select(index => (long)index),
            stored.Select(item => item.SequenceNumber));
        var hasher = new Sha256EventHasher(new CanonicalEventSerializer());
        var previous = HashChain.GenesisHash;
        foreach (var record in stored)
        {
            Assert.Equal(previous, record.PreviousHash);
            Assert.True(record.ToDomain().HasValidContentHash(hasher));
            var receipt = Assert.Single(receipts, item => item.EventId == record.EventId);
            Assert.Equal(receipt.SequenceNumber, record.SequenceNumber);
            Assert.Equal(receipt.ContentHash, record.ContentHash);
            Assert.Equal(receipt.ActorId, record.ActorId);
            Assert.Equal(receipt.Payload.GetProperty("token").GetString(), record.Payload.GetProperty("token").GetString());
            previous = record.ContentHash;
        }

        var projections = await context.AuditEventReadProjections.AsNoTracking().ToListAsync();
        Assert.Equal(stored.Select(row => row.EventId).Order(), projections.Select(row => row.EventId).Order());
        var head = await context.ChainMetadata.SingleAsync();
        Assert.Equal(receipts.Length, head.HeadSequenceNumber);
        Assert.Equal(previous, head.HeadHash);
        var verification = await new PostgresChainVerificationService(
            new ContextFactory(_fixture.ConnectionString), hasher).VerifyAsync();
        Assert.True(verification.IsValid, verification.Detail);
        Assert.Equal(receipts.Length, verification.EventsVerified);
    }

    private static PostgresAuditEventAppendService Service(ContextFactory factory) =>
        new(factory, new Sha256EventHasher(new CanonicalEventSerializer()), new CommitmentPayloadProtector([]));

    private static AppendAuditEventRequest Request(string token) =>
        new("scenario-d", token, "document", "1", JsonSerializer.SerializeToElement(new { token }));

    private sealed class ContextFactory(string connectionString, IInterceptor? interceptor = null)
        : IDbContextFactory<AuditLogDbContext>
    {
        public AuditLogDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AuditLogDbContext>().UseNpgsql(connectionString);
            if (interceptor is not null)
            {
                options.AddInterceptors(interceptor);
            }

            return new AuditLogDbContext(options.Options);
        }

        public Task<AuditLogDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class RejectCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Scenario D injected pre-commit failure.");
    }
}
