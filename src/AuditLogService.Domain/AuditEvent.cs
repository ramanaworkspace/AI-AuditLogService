namespace AuditLogService.Domain;

public sealed class AuditEvent
{
    public AuditEvent(AuditEventData data, string contentHash)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!HashChain.IsValidHash(contentHash))
        {
            throw new ArgumentException("ContentHash must be a lowercase SHA-256 hash.", nameof(contentHash));
        }

        Data = data;
        ContentHash = contentHash;
    }

    public AuditEventData Data { get; }

    public Guid EventId => Data.EventId;

    public long SequenceNumber => Data.SequenceNumber;

    public string EventType => Data.EventType;

    public string ActorId => Data.ActorId;

    public string ResourceType => Data.ResourceType;

    public string ResourceId => Data.ResourceId;

    public System.Text.Json.JsonElement Payload => Data.Payload;

    public DateTimeOffset Timestamp => Data.Timestamp;

    public string PreviousHash => Data.PreviousHash;

    public string ContentHash { get; }

    public static AuditEvent Create(AuditEventData data, IEventHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(hasher);
        return new AuditEvent(data, hasher.ComputeHash(data));
    }

    public bool HasValidContentHash(IEventHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        return string.Equals(ContentHash, hasher.ComputeHash(Data), StringComparison.Ordinal);
    }
}
