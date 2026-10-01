using System.Text.Json;
using AuditLogService.Domain;

namespace AuditLogService.UnitTests;

public sealed class AuditEventValidationTests
{
    [Fact]
    public void ConstructorRejectsEmptyEventId()
    {
        var exception = Assert.Throws<ArgumentException>(() => Create(eventId: Guid.Empty));
        Assert.Equal("eventId", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ConstructorRejectsMissingRequiredText(string? value)
    {
        Assert.Throws<ArgumentException>(() => Create(eventType: value!));
        Assert.Throws<ArgumentException>(() => Create(actorId: value!));
        Assert.Throws<ArgumentException>(() => Create(resourceType: value!));
        Assert.Throws<ArgumentException>(() => Create(resourceId: value!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConstructorRejectsInvalidSequenceNumber(long sequenceNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(sequenceNumber: sequenceNumber));
    }

    [Fact]
    public void ConstructorRejectsNonUtcOrDefaultTimestamp()
    {
        Assert.Throws<ArgumentException>(() => Create(timestamp: DateTimeOffset.MinValue));
        Assert.Throws<ArgumentException>(() => Create(
            timestamp: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(1))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void ConstructorRejectsInvalidPreviousHash(string previousHash)
    {
        Assert.Throws<ArgumentException>(() => Create(previousHash: previousHash));
    }

    [Fact]
    public void EventConstructorRejectsInvalidContentHash()
    {
        Assert.Throws<ArgumentException>(() => new AuditEvent(Create(), "not-a-hash"));
    }

    [Fact]
    public void DataCopiesPayloadToRemainImmutable()
    {
        using var document = JsonDocument.Parse("""{"value":"before"}""");
        var data = Create(payload: document.RootElement);
        document.Dispose();

        Assert.Equal("before", data.Payload.GetProperty("value").GetString());
    }

    private static AuditEventData Create(
        Guid? eventId = null,
        long sequenceNumber = 1,
        string? eventType = "RECORD_UPDATED",
        string? actorId = "actor-1",
        string? resourceType = "DOCUMENT",
        string? resourceId = "document-1",
        JsonElement? payload = null,
        DateTimeOffset? timestamp = null,
        string previousHash = HashChain.GenesisHash)
    {
        using var document = JsonDocument.Parse("""{"value":1}""");
        return new AuditEventData(
            eventId ?? Guid.Parse("00000000-0000-0000-0000-000000000001"),
            sequenceNumber,
            eventType!,
            actorId!,
            resourceType!,
            resourceId!,
            payload ?? document.RootElement,
            timestamp ?? new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            previousHash);
    }
}