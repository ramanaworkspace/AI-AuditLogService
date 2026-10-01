using System.Globalization;
using System.Text;

namespace AuditLogService.Application.Query;

/// <summary>
/// Encodes and decodes the opaque keyset-pagination cursor used by
/// <see cref="IAuditEventQueryService"/>.
/// </summary>
/// <remarks>
/// The cursor is the last-seen <c>SequenceNumber</c> from the previous page, Base64-encoded
/// so it is opaque to callers and safe to round-trip through a query string. Sequence numbers
/// are unique and monotonically assigned by the append process (never reused), so they are a
/// stable keyset cursor: unlike offset-based pagination, the result is not affected by events
/// appended after the cursor was issued.
/// </remarks>
public static class AuditEventCursor
{
    /// <summary>
    /// Encodes <paramref name="sequenceNumber"/> as an opaque cursor token.
    /// </summary>
    public static string Encode(long sequenceNumber) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(sequenceNumber.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// Attempts to decode <paramref name="cursor"/> back into a sequence number.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> and the decoded sequence number when <paramref name="cursor"/> is
    /// a well-formed token produced by <see cref="Encode"/>; otherwise <see langword="false"/>.
    /// </returns>
    public static bool TryDecode(string? cursor, out long sequenceNumber)
    {
        sequenceNumber = 0;

        if (string.IsNullOrEmpty(cursor))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(cursor);
        }
        catch (FormatException)
        {
            return false;
        }

        string text;
        try
        {
            text = Encoding.UTF8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out sequenceNumber)
            && sequenceNumber > 0;
    }
}
