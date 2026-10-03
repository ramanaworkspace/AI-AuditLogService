using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditLogService.Domain;

namespace AuditLogService.Application.Redaction;

public sealed class CommitmentPayloadProtector : IPayloadProtector
{
    public const string Marker = "$auditCommitment";
    public const string RedactedValue = "[REDACTED]";
    private readonly string[][] _paths;

    public CommitmentPayloadProtector(IEnumerable<string> sensitivePaths)
    {
        ArgumentNullException.ThrowIfNull(sensitivePaths);
        _paths = sensitivePaths.Select(ParsePath).ToArray();
        for (var left = 0; left < _paths.Length; left++)
        {
            for (var right = left + 1; right < _paths.Length; right++)
            {
                var shorter = Math.Min(_paths[left].Length, _paths[right].Length);
                if (_paths[left].Take(shorter).SequenceEqual(_paths[right].Take(shorter), StringComparer.Ordinal))
                {
                    throw InvalidConfiguration();
                }
            }
        }
    }

    public JsonElement Protect(JsonElement payload)
    {
        ValidateInput(payload);
        return Transform(payload, true);
    }

    public JsonElement Project(JsonElement committedPayload) => Transform(committedPayload, false);

    private JsonElement Transform(JsonElement payload, bool protect)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, payload, [], protect);
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private void Write(Utf8JsonWriter writer, JsonElement value, string[] path, bool protect)
    {
        var selected = _paths.Any(candidate => candidate.SequenceEqual(path, StringComparer.Ordinal));
        var isCommitment = value.ValueKind == JsonValueKind.Object && value.TryGetProperty(Marker, out _);
        if (selected || (!protect && isCommitment))
        {
            if (!protect)
            {
                writer.WriteStringValue(RedactedValue);
                return;
            }

            var salt = RandomNumberGenerator.GetBytes(32);
            var canonical = CanonicalEventSerializer.SerializeValue(value);
            var prefix = Encoding.UTF8.GetBytes("AuditLogService.PayloadCommitment.v1\0");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(prefix);
            hash.AppendData(salt);
            hash.AppendData(canonical);
            var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
            CryptographicOperations.ZeroMemory(canonical);
            writer.WriteStartObject();
            writer.WritePropertyName(Marker);
            writer.WriteStartObject();
            writer.WriteString("scheme", "sha256-salted-v1");
            writer.WriteString("salt", Convert.ToHexStringLower(salt));
            writer.WriteString("digest", digest);
            writer.WriteEndObject();
            writer.WriteEndObject();
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value, [.. path, property.Name], protect);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    Write(writer, item, [.. path, index.ToString(System.Globalization.CultureInfo.InvariantCulture)], protect);
                    index++;
                }

                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static void ValidateInput(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name == Marker || !names.Add(property.Name))
                {
                    throw new PayloadProtectionException();
                }

                ValidateInput(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                ValidateInput(item);
            }
        }
    }

    private static string[] ParsePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/')
        {
            throw InvalidConfiguration();
        }

        var tokens = path[1..].Split('/');
        foreach (var token in tokens)
        {
            for (var index = 0; index < token.Length; index++)
            {
                if (token[index] == '~' && (++index == token.Length || (token[index] != '0' && token[index] != '1')))
                {
                    throw InvalidConfiguration();
                }
            }
        }

        var decoded = tokens.Select(token => token.Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal)).ToArray();
        if (decoded.Contains(Marker, StringComparer.Ordinal))
        {
            throw InvalidConfiguration();
        }

        return decoded;
    }

    private static ArgumentException InvalidConfiguration() =>
        new("Redaction paths must be non-root JSON pointers with valid escaping and no duplicate or overlapping paths.");
}
