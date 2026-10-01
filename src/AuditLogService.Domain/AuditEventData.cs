using System.Text.Json;

namespace AuditLogService.Domain;

public sealed class AuditEventData
{
    public AuditEventData(
        Guid eventId,
        long sequenceNumber,
        string eventType,
        string actorId,
        string resourceType,
        string resourceId,
        JsonElement payload,
        DateTimeOffset timestamp,
        string previousHash)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("EventId must not be empty.", nameof(eventId));
        }

        if (sequenceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), "SequenceNumber must be positive.");
        }

        EventType = RequireValue(eventType, nameof(eventType));
        ActorId = RequireValue(actorId, nameof(actorId));
        ResourceType = RequireValue(resourceType, nameof(resourceType));
        ResourceId = RequireValue(resourceId, nameof(resourceId));

        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Payload must be a JSON object.", nameof(payload));
        }

        if (timestamp == default || timestamp.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC timestamp.", nameof(timestamp));
        }

        if (!HashChain.IsValidPreviousHash(previousHash))
        {
            throw new ArgumentException("PreviousHash must be GENESIS or a lowercase SHA-256 hash.", nameof(previousHash));
        }

        EventId = eventId;
        SequenceNumber = sequenceNumber;
        Payload = payload.Clone();
        Timestamp = timestamp;
        PreviousHash = previousHash;
    }

    public Guid EventId { get; }

    public long SequenceNumber { get; }

    public string EventType { get; }

    public string ActorId { get; }

    public string ResourceType { get; }

    public string ResourceId { get; }

    public JsonElement Payload { get; }

    public DateTimeOffset Timestamp { get; }

    public string PreviousHash { get; }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} must not be null, empty, or whitespace.", parameterName);
        }

        return value;
    }
}