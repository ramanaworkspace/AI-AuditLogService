using System.Text;
using System.Text.Json;
using AuditLogService.Domain;

namespace AuditLogService.UnitTests;

public sealed class CanonicalEventSerializerTests
{
    private readonly CanonicalEventSerializer _serializer = new();
    private readonly Sha256EventHasher _hasher;

    public CanonicalEventSerializerTests()
    {
        _hasher = new Sha256EventHasher(_serializer);
    }

    [Fact]
    public void SameEventProducesSameCanonicalRepresentationAndHash()
    {
        var first = Create(payloadJson: """{"b":2,"a":1}""");
        var second = Create(payloadJson: """{"a":1.0,"b":2e0}""");

        Assert.Equal(Canonical(first), Canonical(second));
        Assert.Equal(_hasher.ComputeHash(first), _hasher.ComputeHash(second));
    }

    [Theory]
    [InlineData("eventId")]
    [InlineData("sequenceNumber")]
    [InlineData("eventType")]
    [InlineData("actorId")]
    [InlineData("resourceType")]
    [InlineData("resourceId")]
    [InlineData("payload")]
    [InlineData("timestamp")]
    [InlineData("previousHash")]
    public void ChangingAnyHashedFieldChangesHash(string field)
    {
        var original = Create();
        var changed = field switch
        {
            "eventId" => Create(eventId: Guid.Parse("00000000-0000-0000-0000-000000000002")),
            "sequenceNumber" => Create(sequenceNumber: 2),
            "eventType" => Create(eventType: "USER_LOGIN"),
            "actorId" => Create(actorId: "actor-2"),
            "resourceType" => Create(resourceType: "ACCOUNT"),
            "resourceId" => Create(resourceId: "resource-2"),
            "payload" => Create(payloadJson: """{"value":2}"""),
            "timestamp" => Create(timestamp: new DateTimeOffset(2026, 1, 2, 3, 4, 6, TimeSpan.Zero)),
            "previousHash" => Create(previousHash: new string('a', 64)),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        Assert.NotEqual(_hasher.ComputeHash(original), _hasher.ComputeHash(changed));
    }

    [Fact]
    public void GenesisIsDeterministicAndFirstEventUsesGenesisPreviousHash()
    {
        Assert.Equal("GENESIS", HashChain.GenesisHash);
        var first = Create();
        Assert.Equal(HashChain.GenesisHash, first.PreviousHash);

        var firstRecord = AuditEvent.Create(first, _hasher);
        Assert.True(HashChain.HasValidPredecessor(firstRecord, null));
        var second = Create(
            eventId: Guid.Parse("00000000-0000-0000-0000-000000000002"),
            sequenceNumber: 2,
            previousHash: firstRecord.ContentHash);

        Assert.Equal(firstRecord.ContentHash, second.PreviousHash);
        var secondRecord = AuditEvent.Create(second, _hasher);
        Assert.True(HashChain.HasValidPredecessor(secondRecord, firstRecord));
        Assert.Equal(_hasher.ComputeHash(second), secondRecord.ContentHash);
    }

    [Fact]
    public void PropertiesAreSerializedInDefinedOrder()
    {
        var canonical = Canonical(Create());

        Assert.StartsWith(
            "{\"eventId\":\"00000000-0000-0000-0000-000000000001\",\"sequenceNumber\":1,\"eventType\":\"RECORD_UPDATED\",\"actorId\":\"actor-1\",\"resourceType\":\"DOCUMENT\",\"resourceId\":\"resource-1\",\"payload\":",
            canonical,
            StringComparison.Ordinal);
        Assert.True(canonical.IndexOf("\"timestamp\"", StringComparison.Ordinal)
            < canonical.IndexOf("\"previousHash\"", StringComparison.Ordinal));
    }

    [Fact]
    public void PayloadPropertiesAreRecursivelySortedAndNumbersAreNormalized()
    {
        var first = Canonical(Create(payloadJson: """{"z":{"y":2.00,"x":1e0},"a":[3.0,1e1]}"""));
        var second = Canonical(Create(payloadJson: """{"a":[3,10],"z":{"x":1,"y":2}}"""));

        Assert.Equal(first, second);
        Assert.Contains("\"payload\":{\"a\":[3e0,1e1],\"z\":{\"x\":1e0,\"y\":2e0}}", first, StringComparison.Ordinal);
    }

    [Fact]
    public void UnicodePayloadRoundTripsThroughDeterministicUtf8Serialization()
    {
        var data = Create(payloadJson: """{"greeting":"café 🌍"}""");
        var bytes = _serializer.Serialize(data);
        using var document = JsonDocument.Parse(bytes);

        Assert.Equal("café 🌍", document.RootElement.GetProperty("payload").GetProperty("greeting").GetString());
        Assert.Equal(bytes, _serializer.Serialize(data));
    }

    [Fact]
    public void NullPayloadMembersArePreservedAsJsonNull()
    {
        var representation = Canonical(Create(payloadJson: """{"optional":null}"""));

        Assert.Contains("\"payload\":{\"optional\":null}", representation, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicatePayloadPropertiesAreRejected()
    {
        var data = Create(payloadJson: """{"key":1,"key":2}""");

        Assert.Throws<ArgumentException>(() => _serializer.Serialize(data));
    }

    private string Canonical(AuditEventData data) => Encoding.UTF8.GetString(_serializer.Serialize(data));

    private static AuditEventData Create(
        string payloadJson = """{"value":1}""",
        Guid? eventId = null,
        long sequenceNumber = 1,
        string eventType = "RECORD_UPDATED",
        string actorId = "actor-1",
        string resourceType = "DOCUMENT",
        string resourceId = "resource-1",
        DateTimeOffset? timestamp = null,
        string previousHash = HashChain.GenesisHash)
    {
        using var document = JsonDocument.Parse(payloadJson);
        return new AuditEventData(
            eventId ?? Guid.Parse("00000000-0000-0000-0000-000000000001"),
            sequenceNumber,
            eventType,
            actorId,
            resourceType,
            resourceId,
            document.RootElement,
            timestamp ?? new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            previousHash);
    }
}