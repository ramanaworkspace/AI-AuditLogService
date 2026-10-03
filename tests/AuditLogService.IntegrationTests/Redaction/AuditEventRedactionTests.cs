using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AuditLogService.Api.Contracts;
using AuditLogService.Application.Append;
using AuditLogService.Application.Redaction;
using AuditLogService.Application.Verification;
using AuditLogService.Domain;
using AuditLogService.IntegrationTests.Api;
using AuditLogService.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuditLogService.IntegrationTests.Redaction;

[Collection(PostgreSqlCollection.Name)]
public sealed class AuditEventRedactionTests(PostgreSqlFixture fixture)
{
    private const string Secret = "SENSITIVE-ACCOUNT-987654321";
    private readonly PostgreSqlFixture _fixture = fixture;

    [Fact]
    public async Task IngestPersistsOnlyCommitmentsAndSafeProjectionAndVerifiesChain()
    {
        await _fixture.ResetAsync();
        using var logs = new CapturedLogs();
        using var factory = CreateFactory(logs);
        using var client = factory.CreateClient();
        for (var index = 0; index < 2; index++)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/audit-events", Request());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
            var created = JsonSerializer.Deserialize<AuditEventResponse>(text, JsonSerializerOptions.Web);
            Assert.NotNull(created);
            Assert.Equal("[REDACTED]", created.Payload.GetProperty("accountNumber").GetString());
            Assert.Equal("[REDACTED]", created.Payload.GetProperty("person").GetProperty("id").GetString());
            Assert.Equal("readable", created.Payload.GetProperty("description").GetString());
        }

        await using var dbContext = _fixture.CreateDbContext();
        var records = await dbContext.AuditEvents.OrderBy(record => record.SequenceNumber).ToListAsync();
        var projections = await dbContext.AuditEventReadProjections.ToListAsync();
        Assert.Equal(2, projections.Count);
        var hasher = new Sha256EventHasher(new CanonicalEventSerializer());
        foreach (var record in records)
        {
            Assert.DoesNotContain(Secret, record.Payload.GetRawText(), StringComparison.Ordinal);
            Assert.True(record.Payload.GetProperty("accountNumber").TryGetProperty(CommitmentPayloadProtector.Marker, out _));
            Assert.True(record.ToDomain().HasValidContentHash(hasher));
            var projected = projections.Single(projection => projection.EventId == record.EventId).Payload;
            Assert.DoesNotContain(Secret, projected.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(CommitmentPayloadProtector.Marker, projected.GetRawText(), StringComparison.Ordinal);
            var exported = factory.Services.GetRequiredService<IAuditEventExportProjector>().Project(record.ToDomain());
            Assert.DoesNotContain(Secret, exported.GetRawText(), StringComparison.Ordinal);
            Assert.Equal("[REDACTED]", exported.GetProperty("payload").GetProperty("person").GetProperty("id").GetString());
        }

        var page = await client.GetFromJsonAsync<AuditEventListResponse>("/api/v1/audit-events");
        Assert.NotNull(page);
        Assert.All(page.Items, item =>
            Assert.Equal("[REDACTED]", item.Payload.GetProperty("accountNumber").GetString()));
        Assert.Equal(records[0].ContentHash, records[1].PreviousHash);
        var verification = await client.GetFromJsonAsync<ChainVerificationResponse>("/api/v1/audit-events/verify");
        Assert.NotNull(verification);
        Assert.True(verification.IsValid);
        Assert.Equal(2, verification.EventsVerified);
        Assert.DoesNotContain(Secret, string.Join('\n', logs.Messages), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("reserved")]
    [InlineData("missing")]
    [InlineData("malformed")]
    public async Task RejectedPayloadsDoNotLeakValuesToErrorsLogsOrDatabase(string failure)
    {
        await _fixture.ResetAsync();
        using var logs = new CapturedLogs();
        using var factory = CreateFactory(logs);
        using var client = factory.CreateClient();
        var json = failure switch
        {
            "duplicate" => $$$$"""{"eventType":"test","actorId":"actor","resourceType":"doc","resourceId":"1","payload":{"accountNumber":"{{{{Secret}}}}","accountNumber":"{{{{Secret}}}}"}}""",
            "reserved" => $$$$"""{"eventType":"test","actorId":"actor","resourceType":"doc","resourceId":"1","payload":{"accountNumber":{"$auditCommitment":"{{{{Secret}}}}"}}}""",
            "missing" => $$$$"""{"actorId":"actor","resourceType":"doc","resourceId":"1","payload":{"accountNumber":"{{{{Secret}}}}"}}""",
            "malformed" => $$$$"""{"eventType":"test","actorId":"actor","resourceType":"doc","resourceId":"1","payload":{"accountNumber":"{{{{Secret}}}}",}}""",
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/audit-events", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, string.Join('\n', logs.Messages), StringComparison.Ordinal);
        await using var dbContext = _fixture.CreateDbContext();
        Assert.False(await dbContext.AuditEvents.AnyAsync());
        Assert.False(await dbContext.AuditEventReadProjections.AnyAsync());
        Assert.Equal(0, (await dbContext.ChainMetadata.SingleAsync()).HeadSequenceNumber);
    }

    [Fact]
    public async Task DatabaseFailureAfterProtectionLeavesNoEventOrProjectionAndNoValueInLogs()
    {
        await _fixture.ResetAsync();
        using var logs = new CapturedLogs();
        using var factory = CreateFactory(logs);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/audit-events", new
        {
            eventType = "test",
            actorId = new string('a', 301),
            resourceType = "doc",
            resourceId = "1",
            payload = new { accountNumber = Secret }
        });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, string.Join('\n', logs.Messages), StringComparison.Ordinal);
        await using var dbContext = _fixture.CreateDbContext();
        Assert.False(await dbContext.AuditEvents.AnyAsync());
        Assert.False(await dbContext.AuditEventReadProjections.AnyAsync());
        Assert.Equal(0, (await dbContext.ChainMetadata.SingleAsync()).HeadSequenceNumber);
        Assert.Equal(HashChain.GenesisHash, (await dbContext.ChainMetadata.SingleAsync()).HeadHash);
    }

    [Fact]
    public async Task ModifiedCommitmentIsDetectedByVerification()
    {
        await _fixture.ResetAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var createdResponse = await client.PostAsJsonAsync("/api/v1/audit-events", Request());
        createdResponse.EnsureSuccessStatusCode();
        var created = await createdResponse.Content.ReadFromJsonAsync<AuditEventResponse>();
        Assert.NotNull(created);
        await using (var dbContext = _fixture.CreateDbContext())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            await dbContext.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
            var hash = new string('f', 64);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE audit_events SET payload = jsonb_set(payload, '{{accountNumber,$auditCommitment,digest}}', to_jsonb({hash}::text)) WHERE event_id = {created.EventId}");
            await transaction.CommitAsync();
        }

        var result = await client.GetFromJsonAsync<ChainVerificationResponse>("/api/v1/audit-events/verify");
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Equal(created.EventId, result.FirstInconsistentEventId);
        Assert.Equal(ChainViolationType.ContentHashMismatch, result.ViolationType);
    }

    [Theory]
    [InlineData("person.id")]
    [InlineData("/bad~2")]
    public void InvalidConfigurationFailsBeforeAcceptingRequests(string path)
    {
        using var factory = new AuditEventApiFactory
        {
            ConnectionString = _fixture.ConnectionString,
            AdditionalConfiguration = new Dictionary<string, string?> { ["Redaction:SensitivePaths:0"] = path }
        };
        Assert.Throws<ArgumentException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task PolicyRemovalKeepsExistingCommitmentsRedacted()
    {
        await _fixture.ResetAsync();
        using (var factory = CreateFactory())
        using (var client = factory.CreateClient())
        using (var response = await client.PostAsJsonAsync("/api/v1/audit-events", Request()))
        {
            response.EnsureSuccessStatusCode();
        }

        using var changedFactory = new AuditEventApiFactory
        {
            ConnectionString = _fixture.ConnectionString,
            AdditionalConfiguration = new Dictionary<string, string?>
            {
                ["Redaction:SensitivePaths:0"] = "/unused",
                ["Redaction:SensitivePaths:1"] = "/unused2"
            }
        };
        using var changedClient = changedFactory.CreateClient();
        var page = await changedClient.GetFromJsonAsync<AuditEventListResponse>("/api/v1/audit-events");
        Assert.NotNull(page);
        Assert.Equal("[REDACTED]", page.Items[0].Payload.GetProperty("person").GetProperty("id").GetString());
        var verification = await changedClient.GetFromJsonAsync<ChainVerificationResponse>("/api/v1/audit-events/verify");
        Assert.NotNull(verification);
        Assert.True(verification.IsValid);
    }

    [Fact]
    public async Task LegacyPlaintextIsMaskedWithoutRewritingImmutableEvent()
    {
        await _fixture.ResetAsync();
        var payload = JsonSerializer.SerializeToElement(new { accountNumber = Secret, description = "readable" });
        var original = await _fixture.CreateAppendService().AppendAsync(
            new AppendAuditEventRequest("test", "actor", "doc", "1", payload));
        await using (var dbContext = _fixture.CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM audit_event_read_projections WHERE event_id = {original.EventId}");
        }

        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var page = await client.GetFromJsonAsync<AuditEventListResponse>("/api/v1/audit-events");
        Assert.NotNull(page);
        Assert.Equal("[REDACTED]", page.Items[0].Payload.GetProperty("accountNumber").GetString());
        Assert.Equal("readable", page.Items[0].Payload.GetProperty("description").GetString());
        var exported = factory.Services.GetRequiredService<IAuditEventExportProjector>().Project(original);
        Assert.DoesNotContain(Secret, exported.GetRawText(), StringComparison.Ordinal);
        await using var context = _fixture.CreateDbContext();
        var stored = await context.AuditEvents.SingleAsync();
        Assert.Equal(original.ContentHash, stored.ContentHash);
        Assert.Equal(Secret, stored.Payload.GetProperty("accountNumber").GetString());
        Assert.False(await context.AuditEventReadProjections.AnyAsync());
        var verification = await client.GetFromJsonAsync<ChainVerificationResponse>("/api/v1/audit-events/verify");
        Assert.NotNull(verification);
        Assert.True(verification.IsValid);
    }

    [Fact]
    public async Task AlteredProjectionIsRejectedWithoutExposingInjectedValues()
    {
        await _fixture.ResetAsync();
        using var logs = new CapturedLogs();
        using var factory = CreateFactory(logs);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/audit-events", Request());
        response.EnsureSuccessStatusCode();
        await using (var context = _fixture.CreateDbContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE audit_event_read_projections SET payload = jsonb_set(payload, '{{description}}', to_jsonb({Secret}::text))");
        }

        using var queryResponse = await client.GetAsync("/api/v1/audit-events");
        Assert.Equal(HttpStatusCode.InternalServerError, queryResponse.StatusCode);
        Assert.DoesNotContain(Secret, await queryResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, string.Join('\n', logs.Messages), StringComparison.Ordinal);
        var verification = await client.GetFromJsonAsync<ChainVerificationResponse>("/api/v1/audit-events/verify");
        Assert.NotNull(verification);
        Assert.True(verification.IsValid);
    }

    [Theory]
    [InlineData("Redaction:SensitivePaths", "/accountNumber")]
    [InlineData("Redaction:SensitivePaths:unexpected", "/accountNumber")]
    [InlineData("Redaction:SensitivePaths:0:nested", "/accountNumber")]
    public void NonArrayConfigurationFailsBeforeAcceptingRequests(string key, string value)
    {
        using var factory = new AuditEventApiFactory
        {
            ConnectionString = _fixture.ConnectionString,
            AdditionalConfiguration = new Dictionary<string, string?> { [key] = value }
        };
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    private AuditEventApiFactory CreateFactory(ILoggerProvider? logs = null) => new()
    {
        ConnectionString = _fixture.ConnectionString,
        LogProvider = logs,
        AdditionalConfiguration = new Dictionary<string, string?>
        {
            ["Redaction:SensitivePaths:0"] = "/accountNumber",
            ["Redaction:SensitivePaths:1"] = "/person/id"
        }
    };

    private static object Request() => new
    {
        eventType = "test",
        actorId = "actor",
        resourceType = "doc",
        resourceId = "1",
        payload = new { accountNumber = Secret, person = new { id = Secret }, description = "readable" }
    };

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturedLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturedLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
                if (exception is not null)
                {
                    messages.Enqueue(exception.ToString());
                }
            }
        }
    }
}
