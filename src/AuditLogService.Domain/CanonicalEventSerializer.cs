using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace AuditLogService.Domain;

public sealed class CanonicalEventSerializer : ICanonicalEventSerializer
{
    public byte[] Serialize(AuditEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var builder = new StringBuilder();
        builder.Append('{');
        WritePropertyName(builder, "eventId");
        WriteString(builder, eventData.EventId.ToString("D"));
        builder.Append(',');
        WritePropertyName(builder, "sequenceNumber");
        builder.Append(eventData.SequenceNumber.ToString(CultureInfo.InvariantCulture));
        builder.Append(',');
        WritePropertyName(builder, "eventType");
        WriteString(builder, eventData.EventType);
        builder.Append(',');
        WritePropertyName(builder, "actorId");
        WriteString(builder, eventData.ActorId);
        builder.Append(',');
        WritePropertyName(builder, "resourceType");
        WriteString(builder, eventData.ResourceType);
        builder.Append(',');
        WritePropertyName(builder, "resourceId");
        WriteString(builder, eventData.ResourceId);
        builder.Append(',');
        WritePropertyName(builder, "payload");
        WriteCanonicalValue(builder, eventData.Payload);
        builder.Append(',');
        WritePropertyName(builder, "timestamp");
        WriteString(builder, eventData.Timestamp.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            CultureInfo.InvariantCulture));
        builder.Append(',');
        WritePropertyName(builder, "previousHash");
        WriteString(builder, eventData.PreviousHash);
        builder.Append('}');

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] SerializeValue(JsonElement value)
    {
        var builder = new StringBuilder();
        WriteCanonicalValue(builder, value);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void WriteCanonicalValue(StringBuilder builder, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var properties = value.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToArray();

                for (var index = 0; index < properties.Length; index++)
                {
                    if (index > 0 && string.Equals(
                        properties[index - 1].Name,
                        properties[index].Name,
                        StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "Payload contains duplicate property names.",
                            nameof(value));
                    }

                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    WriteString(builder, properties[index].Name);
                    builder.Append(':');
                    WriteCanonicalValue(builder, properties[index].Value);
                }

                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!firstItem)
                    {
                        builder.Append(',');
                    }

                    WriteCanonicalValue(builder, item);
                    firstItem = false;
                }

                builder.Append(']');
                break;
            case JsonValueKind.String:
                WriteString(builder, value.GetString()!);
                break;
            case JsonValueKind.Number:
                builder.Append(NormalizeNumber(value.GetRawText()));
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            default:
                throw new ArgumentException("Payload contains an unsupported JSON value.", nameof(value));
        }
    }

    private static void WritePropertyName(StringBuilder builder, string name)
    {
        WriteString(builder, name);
        builder.Append(':');
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < ' ' || character > '~')
                    {
                        builder.Append("\\u");
                        builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static string NormalizeNumber(string rawNumber)
    {
        var exponentSeparator = rawNumber.IndexOfAny(['e', 'E']);
        var mantissa = exponentSeparator < 0 ? rawNumber : rawNumber[..exponentSeparator];
        var exponentText = exponentSeparator < 0 ? "0" : rawNumber[(exponentSeparator + 1)..];
        var exponent = BigInteger.Parse(exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var isNegative = mantissa[0] == '-';

        if (isNegative)
        {
            mantissa = mantissa[1..];
        }

        var decimalPoint = mantissa.IndexOf('.');
        var fractionalDigits = decimalPoint < 0 ? 0 : mantissa.Length - decimalPoint - 1;
        var digits = decimalPoint < 0 ? mantissa : string.Concat(mantissa.AsSpan(0, decimalPoint), mantissa.AsSpan(decimalPoint + 1));
        digits = digits.TrimStart('0');

        if (digits.Length == 0)
        {
            return "0";
        }

        var scale = exponent - fractionalDigits;
        var trailingZeroCount = digits.Length - digits.TrimEnd('0').Length;
        if (trailingZeroCount > 0)
        {
            digits = digits[..^trailingZeroCount];
            scale += trailingZeroCount;
        }

        var scientificExponent = scale + digits.Length - 1;
        var significand = digits.Length == 1
            ? digits
            : string.Concat(digits[0], ".", digits[1..]);
        var sign = isNegative ? "-" : string.Empty;

        return string.Concat(
            sign,
            significand,
            "e",
            scientificExponent.ToString(CultureInfo.InvariantCulture));
    }
}