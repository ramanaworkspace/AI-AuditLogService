using System.Text.Json;
using AuditLogService.Application.Verification;
using AuditLogService.Domain;

namespace AuditLogService.UnitTests.Verification;

public sealed class ChainVerifierTests
{
    private const string AlternateHash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

    private static readonly IEventHasher Hasher = new Sha256EventHasher(new CanonicalEventSerializer());

    [Fact]
    public void ValidChainReportsIsValidAndEventsVerifiedCount()
    {
        var chain = BuildValidChain(3);

        var result = ChainVerifier.Verify(chain, Hasher);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.EventsVerified);
        Assert.Null(result.FirstInconsistentEventId);
        Assert.Null(result.FirstInconsistentSequenceNumber);
        Assert.Null(result.ViolationType);
    }

    [Fact]
    public void EmptyChainIsValid()
    {
        var result = ChainVerifier.Verify([], Hasher);

        Assert.True(result.IsValid);
        Assert.Equal(0, result.EventsVerified);
    }

    [Fact]
    public void FirstRecordMustUseGenesisPreviousHash()
    {
        var chain = BuildValidChain(2);
        var tampered = chain.ToList();
        tampered[0] = ReplacePreviousHash(tampered[0], AlternateHash);

        var result = ChainVerifier.Verify(tampered, Hasher);

        Assert.False(result.IsValid);
        Assert.Equal(0, result.EventsVerified);
        Assert.Equal(tampered[0].EventId, result.FirstInconsistentEventId);
        Assert.Equal(1, result.FirstInconsistentSequenceNumber);
        Assert.Equal(ChainViolationType.InvalidGenesisRelationship, result.ViolationType);
    }

    [Fact]
    public void ContentHashMismatchIsDetected()
    {
        var chain = BuildValidChain(2);
        var tampered = chain.ToList();

        // Simulate storage-level tampering: the stored ContentHash no longer matches what
        // recomputing the canonical hash over the (unchanged) event fields would produce.
        tampered[1] = new AuditEvent(tampered[1].Data, AlternateHash);

        var result = ChainVerifier.Verify(tampered, Hasher);

        Assert.False(result.IsValid);
        Assert.Equal(1, result.EventsVerified);
        Assert.Equal(tampered[1].EventId, result.FirstInconsistentEventId);
        Assert.Equal(2, result.FirstInconsistentSequenceNumber);
        Assert.Equal(ChainViolationType.ContentHashMismatch, result.ViolationType);
    }

    [Fact]
    public void PreviousHashMismatchIsDetected()
    {
        var chain = BuildValidChain(3);
        var tampered = chain.ToList();
        tampered[2] = ReplacePreviousHash(tampered[2], AlternateHash);

        var result = ChainVerifier.Verify(tampered, Hasher);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.EventsVerified);
        Assert.Equal(tampered[2].EventId, result.FirstInconsistentEventId);
        Assert.Equal(3, result.FirstInconsistentSequenceNumber);
        Assert.Equal(ChainViolationType.PreviousHashMismatch, result.ViolationType);
    }

    [Fact]
    public void SequenceGapIsDetected()
    {
        var chain = BuildValidChain(3);
        var withGap = new[] { chain[0], chain[2] };

        var result = ChainVerifier.Verify(withGap, Hasher);

        Assert.False(result.IsValid);
        Assert.Equal(1, result.EventsVerified);
        Assert.Equal(chain[2].EventId, result.FirstInconsistentEventId);
        Assert.Equal(3, result.FirstInconsistentSequenceNumber);
        Assert.Equal(ChainViolationType.SequenceGap, result.ViolationType);
    }

    [Fact]
    public void DuplicateSequenceNumberIsDetected()
    {
        var chain = BuildValidChain(2);
        var duplicated = new[] { chain[0], chain[1], chain[1] };

        var result = ChainVerifier.Verify(duplicated, Hasher);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.EventsVerified);
        Assert.Equal(chain[1].EventId, result.FirstInconsistentEventId);
        Assert.Equal(2, result.FirstInconsistentSequenceNumber);
        Assert.Equal(ChainViolationType.DuplicateSequenceNumber, result.ViolationType);
    }

    private static List<AuditEvent> BuildValidChain(int length)
    {
        using var document = JsonDocument.Parse("""{"value":1}""");
        var payload = document.RootElement;

        var events = new List<AuditEvent>(length);
        var previousHash = HashChain.GenesisHash;
        for (var i = 1; i <= length; i++)
        {
            var data = new AuditEventData(
                Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
                i,
                "RECORD_UPDATED",
                "actor-1",
                "DOCUMENT",
                $"document-{i}",
                payload,
                new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddSeconds(i),
                previousHash);

            var auditEvent = AuditEvent.Create(data, Hasher);
            events.Add(auditEvent);
            previousHash = auditEvent.ContentHash;
        }

        return events;
    }

    private static AuditEvent ReplacePreviousHash(AuditEvent source, string previousHash)
    {
        var data = new AuditEventData(
            source.EventId,
            source.SequenceNumber,
            source.EventType,
            source.ActorId,
            source.ResourceType,
            source.ResourceId,
            source.Payload,
            source.Timestamp,
            previousHash);

        // The recomputed content hash intentionally no longer matches the stored
        // ContentHash below, mirroring a chain whose linkage has been tampered with.
        return new AuditEvent(data, source.ContentHash);
    }
}
