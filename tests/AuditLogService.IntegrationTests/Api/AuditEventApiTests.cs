using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditLogService.Api.Contracts;
using AuditLogService.Application.Verification;
using AuditLogService.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditLogService.IntegrationTests.Api;

/// <summary>
/// End-to-end HTTP tests for the Scenario A Minimal API endpoints: append, query, and chain
/// verification. Exercises the real application pipeline (routing, model binding, validation,
/// and the PostgreSQL-backed services) against the shared integration-test database.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class AuditEventApiTests : IDisposable
{
    private readonly PostgreSqlFixture _fixture;
    private readonly AuditEventApiFactory _factory;
    private readonly HttpClient _client;

    public AuditEventApiTests(PostgreSqlFixture fixture)
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

    private static object CreateRequestBody(
        string eventType = "document.created",
        string actorId = "actor-1",
        string resourceType = "document",
        string resourceId = "doc-1",
        object? payload = null) =>
        new
        {
            eventType,
            actorId,
            resourceType,
            resourceId,
            payload = payload ?? new { title = "Example" }
        };

    [Fact]
    public async Task PostCreatesEventWithServerAssignedChainFields()
    {
        await _fixture.ResetAsync();

        using var response = await _client.PostAsJsonAsync(
            "/api/v1/audit-events",
            CreateRequestBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuditEventResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.EventId);
        Assert.Equal(1, body.SequenceNumber);
        Assert.Equal("GENESIS", body.PreviousHash);
        Assert.Matches("^[0-9a-f]{64}$", body.ContentHash);
        Assert.True(body.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.True(body.Timestamp <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task PostIgnoresClientSuppliedChainFields()
    {
        await _fixture.ResetAsync();

        var payload = new
        {
            eventId = Guid.NewGuid(),
            sequenceNumber = 999,
            eventType = "document.created",
            actorId = "actor-1",
            resourceType = "document",
            resourceId = "doc-1",
            payload = new { title = "Example" },
            timestamp = DateTimeOffset.UtcNow.AddYears(-10),
            previousHash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
            contentHash = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"
        };

        using var response = await _client.PostAsJsonAsync("/api/v1/audit-events", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuditEventResponse>();
        Assert.NotNull(body);
        Assert.Equal(1, body!.SequenceNumber);
        Assert.Equal("GENESIS", body.PreviousHash);
        Assert.NotEqual(
            "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            body.ContentHash);
        Assert.True(body.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Theory]
    [InlineData("eventType")]
    [InlineData("actorId")]
    [InlineData("resourceType")]
    [InlineData("resourceId")]
    public async Task PostReturnsValidationProblemWhenRequiredFieldMissing(string missingField)
    {
        await _fixture.ResetAsync();

        var body = new Dictionary<string, object?>
        {
            ["eventType"] = "document.created",
            ["actorId"] = "actor-1",
            ["resourceType"] = "document",
            ["resourceId"] = "doc-1",
            ["payload"] = new { title = "Example" }
        };
        body[missingField] = null;

        using var response = await _client.PostAsJsonAsync("/api/v1/audit-events", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(missingField, out _));
    }

    [Fact]
    public async Task PostReturnsValidationProblemWhenPayloadIsNotAnObject()
    {
        await _fixture.ResetAsync();

        using var response = await _client.PostAsJsonAsync(
            "/api/v1/audit-events",
            CreateRequestBody(payload: "not-an-object"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("payload", out _));
    }

    [Fact]
    public async Task GetFiltersByActorResourceAndEventType()
    {
        await _fixture.ResetAsync();

        await _client.PostAsJsonAsync(
            "/api/v1/audit-events",
            CreateRequestBody(eventType: "document.created", actorId: "actor-1", resourceType: "document", resourceId: "doc-1"));
        await _client.PostAsJsonAsync(
            "/api/v1/audit-events",
            CreateRequestBody(eventType: "document.updated", actorId: "actor-2", resourceType: "document", resourceId: "doc-2"));
        await _client.PostAsJsonAsync(
            "/api/v1/audit-events",
            CreateRequestBody(eventType: "user.login", actorId: "actor-1", resourceType: "user", resourceId: "user-1"));

        using var response = await _client.GetAsync("/api/v1/audit-events?actorId=actor-1");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuditEventListResponse>();

        Assert.NotNull(body);
        Assert.Equal(2, body!.Items.Count);
        Assert.All(body.Items, item => Assert.Equal("actor-1", item.ActorId));
    }

    [Fact]
    public async Task GetFiltersByTimestampRangeInclusively()
    {
        await _fixture.ResetAsync();

        using var firstResponse = await _client.PostAsJsonAsync(
            "/api/v1/audit-events",
            CreateRequestBody());
        var first = await firstResponse.Content.ReadFromJsonAsync<AuditEventResponse>();
        Assert.NotNull(first);

        using var response = await _client.GetAsync(
            $"/api/v1/audit-events?startTimestamp={Uri.EscapeDataString(first!.Timestamp.ToString("O"))}&endTimestamp={Uri.EscapeDataString(first.Timestamp.ToString("O"))}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuditEventListResponse>();

        Assert.NotNull(body);
        Assert.Single(body!.Items);
        Assert.Equal(first.EventId, body.Items[0].EventId);
    }

    [Fact]
    public async Task GetReturnsValidationProblemWhenStartTimestampAfterEndTimestamp()
    {
        await _fixture.ResetAsync();

        var start = DateTimeOffset.UtcNow;
        var end = start.AddDays(-1);

        using var response = await _client.GetAsync(
            $"/api/v1/audit-events?startTimestamp={Uri.EscapeDataString(start.ToString("O"))}&endTimestamp={Uri.EscapeDataString(end.ToString("O"))}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetReturnsValidationProblemForInvalidCursor()
    {
        await _fixture.ResetAsync();

        using var response = await _client.GetAsync("/api/v1/audit-events?cursor=not-a-valid-cursor");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetReturnsValidationProblemWhenLimitOutOfRange()
    {
        await _fixture.ResetAsync();

        using var response = await _client.GetAsync("/api/v1/audit-events?limit=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPaginatesUsingNextCursor()
    {
        await _fixture.ResetAsync();

        for (var i = 0; i < 3; i++)
        {
            await _client.PostAsJsonAsync(
                "/api/v1/audit-events",
                CreateRequestBody(resourceId: $"doc-{i}"));
        }

        using var firstPageResponse = await _client.GetAsync("/api/v1/audit-events?limit=2");
        firstPageResponse.EnsureSuccessStatusCode();
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<AuditEventListResponse>();

        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage!.Items.Count);
        Assert.NotNull(firstPage.NextCursor);

        using var secondPageResponse = await _client.GetAsync(
            $"/api/v1/audit-events?limit=2&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}");
        secondPageResponse.EnsureSuccessStatusCode();
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<AuditEventListResponse>();

        Assert.NotNull(secondPage);
        Assert.Single(secondPage!.Items);
        Assert.Null(secondPage.NextCursor);
        Assert.DoesNotContain(secondPage.Items, item => firstPage.Items.Any(f => f.EventId == item.EventId));
    }

    [Fact]
    public async Task VerifyReportsValidForAnIntactChain()
    {
        await _fixture.ResetAsync();

        for (var i = 0; i < 3; i++)
        {
            await _client.PostAsJsonAsync(
                "/api/v1/audit-events",
                CreateRequestBody(resourceId: $"doc-{i}"));
        }

        using var response = await _client.GetAsync("/api/v1/audit-events/verify");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChainVerificationResponse>();

        Assert.NotNull(body);
        Assert.True(body!.IsValid);
        Assert.Equal(3, body.EventsVerified);
        Assert.Null(body.FirstInconsistentEventId);
        Assert.Null(body.ViolationType);
    }

    [Fact]
    public async Task VerifyReturnsValidForEmptyChain()
    {
        await _fixture.ResetAsync();

        using var response = await _client.GetAsync("/api/v1/audit-events/verify");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChainVerificationResponse>();

        Assert.NotNull(body);
        Assert.True(body!.IsValid);
        Assert.Equal(0, body.EventsVerified);
    }

    [Fact]
    public async Task VerifyDetectsContentHashTamperingAndIdentifiesTheAffectedEvent()
    {
        await _fixture.ResetAsync();

        AuditEventResponse? second = null;
        for (var i = 0; i < 3; i++)
        {
            using var postResponse = await _client.PostAsJsonAsync(
                "/api/v1/audit-events",
                CreateRequestBody(resourceId: $"doc-{i}"));
            var created = await postResponse.Content.ReadFromJsonAsync<AuditEventResponse>();
            if (i == 1)
            {
                second = created;
            }
        }

        Assert.NotNull(second);

        // Simulate tampering by directly rewriting the stored content hash for the second
        // record, bypassing the application (which never allows updates). The table is
        // protected by an append-only trigger (trg_audit_events_append_only) that rejects
        // UPDATE/DELETE in normal operation; disabling session-level triggers for the
        // duration of this one connection lets the test simulate tampering without
        // weakening that production safeguard.
        await using (var dbContext = _fixture.CreateDbContext())
        {
            // EF Core opens/closes a pooled connection per command by default, and
            // "SET session_replication_role" only affects the connection/session it runs
            // on. Explicitly opening the connection here keeps all three statements on the
            // same backend session so the trigger is actually disabled for the UPDATE.
            await dbContext.Database.OpenConnectionAsync();
            try
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SET session_replication_role = replica");
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE audit_events SET content_hash = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' WHERE sequence_number = {second!.SequenceNumber}");
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SET session_replication_role = origin");
            }
            finally
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }

        using var response = await _client.GetAsync("/api/v1/audit-events/verify");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChainVerificationResponse>();

        Assert.NotNull(body);
        Assert.False(body!.IsValid);
        Assert.Equal(second.EventId, body.FirstInconsistentEventId);
        Assert.Equal(second.SequenceNumber, body.FirstInconsistentSequenceNumber);
        Assert.Equal(ChainViolationType.ContentHashMismatch, body.ViolationType);
    }
}
