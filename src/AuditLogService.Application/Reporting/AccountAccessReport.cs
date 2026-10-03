using System.Globalization;
using System.Text.Json;
using AuditLogService.Domain;

namespace AuditLogService.Application.Reporting;

/// <summary>Validated inclusive UTC acceptance-time interval for the prototype report.</summary>
public sealed record AccountAccessReportQuery
{
    public AccountAccessReportQuery(DateTimeOffset startTime, DateTimeOffset endTime,
        string resourceType = AccountAccessPolicy.ResourceType)
    {
        if (startTime > endTime)
        {
            throw new ArgumentException("startTime must not be after endTime.");
        }

        if (resourceType != AccountAccessPolicy.ResourceType)
        {
            throw new ArgumentException("Only CLIENT_ACCOUNT resources are supported.");
        }

        StartTime = startTime.ToUniversalTime();
        EndTime = endTime.ToUniversalTime();
    }

    public DateTimeOffset StartTime { get; }
    public DateTimeOffset EndTime { get; }
}

public static class AccountAccessPolicy
{
    public const string ResourceType = "CLIENT_ACCOUNT";
    public const string Succeeded = "CLIENT_ACCOUNT_ACCESS_SUCCEEDED";
    public const string Denied = "CLIENT_ACCOUNT_ACCESS_DENIED";
    public const string Failed = "CLIENT_ACCOUNT_ACCESS_FAILED";

    public static string? Outcome(string eventType) => eventType switch
    {
        Succeeded => "succeeded",
        Denied => "denied",
        Failed => "failed",
        _ => null
    };
}

/// <summary>A redacted recorded account-access event, not an authenticated identity assertion.</summary>
public sealed record AccountAccessReportItem(
    Guid EventId, long SequenceNumber, string EventType, string Outcome,
    string ActorId, string ResourceType, string ResourceId, JsonElement Payload,
    string Timestamp, string PreviousHash, string ContentHash, bool IsArchived, string? ArchivedAt);

/// <summary>Global chain verification and head metadata from the report's database snapshot.</summary>
public sealed record AccountAccessChainStatus(
    bool IsValid, long EventsVerified, long ArchivedEventsVerified,
    long HeadSequenceNumber, string HeadHash, Guid? FirstInconsistentEventId,
    long? FirstInconsistentSequenceNumber, string? ViolationType, string? Detail);

/// <summary>Deterministic prototype account-access report including global-chain status.</summary>
public sealed record AccountAccessReport(
    string SchemaVersion, string ResourceType, string StartTime, string EndTime,
    AccountAccessChainStatus ChainStatus, IReadOnlyList<AccountAccessReportItem> Items)
{
    public static string UtcText(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    public byte[] ToJsonBytes()
    {
        var value = JsonSerializer.SerializeToElement(this, JsonSerializerOptions.Web);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default, Indented = false }))
        {
            WriteReportJson(writer, value);
        }

        return stream.ToArray();
    }

    private static void WriteReportJson(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                if (property.Name == "payload")
                {
                    writer.WriteRawValue(CanonicalEventSerializer.SerializeValue(property.Value));
                }
                else
                {
                    WriteReportJson(writer, property.Value);
                }
            }

            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
            {
                WriteReportJson(writer, item);
            }

            writer.WriteEndArray();
        }
        else
        {
            value.WriteTo(writer);
        }
    }
}

public interface IAccountAccessReportService
{
    Task<AccountAccessReport> CreateAsync(AccountAccessReportQuery query, CancellationToken cancellationToken = default);
}
