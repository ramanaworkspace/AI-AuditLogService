using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditLogService.Application.Redaction;
using AuditLogService.Domain;

namespace AuditLogService.UnitTests.Redaction;

public sealed class CommitmentPayloadProtectorTests
{
    [Theory]
    [InlineData("/accountNumber", """{"accountNumber":"SECRET","visible":"ok"}""")]
    [InlineData("/person/id", """{"person":{"id":"SECRET"},"visible":"ok"}""")]
    [InlineData("/items/0/id", """{"items":[{"id":"SECRET"}],"visible":"ok"}""")]
    [InlineData("/a~1b/~0id", """{"a/b":{"~id":"SECRET"},"visible":"ok"}""")]
    public void ConfiguredPathsProduceCommitmentsAndRedactedProjection(string path, string json)
    {
        var protector = new CommitmentPayloadProtector([path]);
        var payload = JsonSerializer.Deserialize<JsonElement>(json);
        var committed = protector.Protect(payload);
        Assert.DoesNotContain("SECRET", committed.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(CommitmentPayloadProtector.Marker, committed.GetRawText(), StringComparison.Ordinal);
        var projected = protector.Project(committed);
        Assert.DoesNotContain("SECRET", projected.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(CommitmentPayloadProtector.Marker, projected.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", projected.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("ok", projected.GetProperty("visible").GetString());
    }

    [Fact]
    public void MultipleValuesIncludingObjectsAndNullAreProtected()
    {
        var protector = new CommitmentPayloadProtector(["/id", "/person", "/nullable"]);
        var payload = JsonSerializer.Deserialize<JsonElement>(
            """{"id":1234567,"person":{"name":"SECRET"},"nullable":null,"visible":[true,42]}""");
        var committed = protector.Protect(payload);
        Assert.DoesNotContain("SECRET", committed.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("1234567", committed.GetRawText(), StringComparison.Ordinal);
        var projected = protector.Project(committed);
        Assert.Equal("[REDACTED]", projected.GetProperty("nullable").GetString());
        Assert.Equal("[REDACTED]", projected.GetProperty("person").GetString());
        Assert.Equal("[true,42]", projected.GetProperty("visible").GetRawText());
    }

    [Fact]
    public void CommitmentMatchesDocumentedConstructionAndUsesFreshSalt()
    {
        var protector = new CommitmentPayloadProtector(["/id"]);
        var payload = JsonSerializer.Deserialize<JsonElement>("""{"id":"SECRET"}""");
        var first = protector.Protect(payload).GetProperty("id").GetProperty(CommitmentPayloadProtector.Marker);
        var second = protector.Protect(payload).GetProperty("id").GetProperty(CommitmentPayloadProtector.Marker);
        Assert.Equal("sha256-salted-v1", first.GetProperty("scheme").GetString());
        var salt = Convert.FromHexString(first.GetProperty("salt").GetString()!);
        Assert.Equal(32, salt.Length);
        Assert.NotEqual(first.GetProperty("salt").GetString(), second.GetProperty("salt").GetString());
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("AuditLogService.PayloadCommitment.v1\0"));
        hash.AppendData(salt);
        hash.AppendData(CanonicalEventSerializer.SerializeValue(payload.GetProperty("id")));
        Assert.Equal(Convert.ToHexStringLower(hash.GetHashAndReset()), first.GetProperty("digest").GetString());
    }

    [Fact]
    public void ReadPolicyRemovalDoesNotRevealStoredCommitments()
    {
        var protector = new CommitmentPayloadProtector(["/id"]);
        var committed = protector.Protect(JsonSerializer.Deserialize<JsonElement>("""{"id":"SECRET"}"""));
        var projected = new CommitmentPayloadProtector([]).Project(committed);
        Assert.Equal("[REDACTED]", projected.GetProperty("id").GetString());
    }

    [Fact]
    public void NonSensitiveAndMissingPathsPreserveContent()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("""{"visible":{"number":1.00,"nullable":null}}""");
        var protector = new CommitmentPayloadProtector(["/missing"]);
        Assert.Equal(CanonicalEventSerializer.SerializeValue(payload),
            CanonicalEventSerializer.SerializeValue(protector.Protect(payload)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("person.id")]
    [InlineData("/bad~")]
    [InlineData("/bad~2")]
    [InlineData("/$auditCommitment")]
    public void InvalidPathsFailWithSafeError(string path)
    {
        var error = Assert.Throws<ArgumentException>(() => new CommitmentPayloadProtector([path]));
        Assert.DoesNotContain(path == "" ? "NEVER_PRESENT" : path, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/id", "/id")]
    [InlineData("/person", "/person/id")]
    public void DuplicateOrOverlappingPathsAreRejected(string first, string second)
    {
        Assert.Throws<ArgumentException>(() => new CommitmentPayloadProtector([first, second]));
    }

    [Theory]
    [InlineData("""{"id":"SECRET","id":"SECRET"}""")]
    [InlineData("""{"id":{"SECRET":1,"SECRET":2}}""")]
    [InlineData("""{"id":{"$auditCommitment":{"digest":"SECRET"}}}""")]
    public void AmbiguousOrReservedInputRejectsWithoutValueOrPropertyDisclosure(string json)
    {
        var protector = new CommitmentPayloadProtector(["/id"]);
        var error = Assert.Throws<PayloadProtectionException>(
            () => protector.Protect(JsonSerializer.Deserialize<JsonElement>(json)));
        Assert.DoesNotContain("SECRET", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExportProjectionContainsNoPlaintextAndRetainsReadableFields()
    {
        var protector = new CommitmentPayloadProtector(["/id"]);
        var committed = protector.Protect(JsonSerializer.Deserialize<JsonElement>("""{"id":"SECRET","visible":"ok"}"""));
        var data = new AuditEventData(Guid.NewGuid(), 1, "test", "actor", "document", "1",
            committed, new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), HashChain.GenesisHash);
        var hasher = new Sha256EventHasher(new CanonicalEventSerializer());
        var auditEvent = AuditEvent.Create(data, hasher);
        var exported = new AuditEventExportProjector(protector).Project(auditEvent);
        Assert.DoesNotContain("SECRET", exported.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("[REDACTED]", exported.GetProperty("payload").GetProperty("id").GetString());
        Assert.Equal("ok", exported.GetProperty("payload").GetProperty("visible").GetString());
        Assert.True(auditEvent.HasValidContentHash(hasher));
    }
}
