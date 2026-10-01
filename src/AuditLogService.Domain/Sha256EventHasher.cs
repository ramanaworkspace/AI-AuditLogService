using System.Security.Cryptography;

namespace AuditLogService.Domain;

public sealed class Sha256EventHasher(ICanonicalEventSerializer serializer) : IEventHasher
{
    private readonly ICanonicalEventSerializer _serializer =
        serializer ?? throw new ArgumentNullException(nameof(serializer));

    public string ComputeHash(AuditEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        var canonicalBytes = _serializer.Serialize(eventData);
        return Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
    }
}