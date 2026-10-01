namespace AuditLogService.Domain;

public static class HashChain
{
    public const string GenesisHash = "GENESIS";

    public static bool HasValidPredecessor(AuditEvent current, AuditEvent? previous)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (previous is null)
        {
            return current.SequenceNumber == 1 && current.PreviousHash == GenesisHash;
        }

        return previous.SequenceNumber < long.MaxValue
            && current.SequenceNumber == previous.SequenceNumber + 1
            && string.Equals(current.PreviousHash, previous.ContentHash, StringComparison.Ordinal);
    }

    public static bool IsValidPreviousHash(string? value) =>
        value == GenesisHash || IsValidHash(value);

    public static bool IsValidHash(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
