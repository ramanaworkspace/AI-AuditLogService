using AuditLogService.Domain;

namespace AuditLogService.Application.Verification;

/// <summary>
/// Pure, storage-independent algorithm that walks a sequence-ordered stream of audit events
/// and determines whether the hash chain they form is internally consistent.
/// </summary>
/// <remarks>
/// Decoupling the walk from <see cref="IChainVerificationService"/>'s storage access lets the
/// chain-consistency rules be exercised directly against hand-constructed
/// <see cref="AuditEvent"/> sequences in unit tests, without a database. Implementations of
/// <see cref="IChainVerificationService"/> are expected to supply events already ordered
/// ascending by <see cref="AuditEvent.SequenceNumber"/>.
/// </remarks>
public static class ChainVerifier
{
    /// <summary>
    /// Walks <paramref name="eventsOrderedBySequence"/> and reports the first inconsistency
    /// found, checking in order, for every event: duplicate sequence number, sequence gap,
    /// genesis/previous-hash linkage, then content-hash correctness.
    /// </summary>
    public static ChainVerificationResult Verify(IEnumerable<AuditEvent> eventsOrderedBySequence, IEventHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(eventsOrderedBySequence);
        ArgumentNullException.ThrowIfNull(hasher);

        AuditEvent? previous = null;
        var expectedSequenceNumber = 1L;
        var verifiedCount = 0L;

        foreach (var current in eventsOrderedBySequence)
        {
            if (previous is not null && current.SequenceNumber == previous.SequenceNumber)
            {
                return ChainVerificationResult.Invalid(
                    verifiedCount,
                    current.EventId,
                    current.SequenceNumber,
                    ChainViolationType.DuplicateSequenceNumber,
                    $"Sequence number {current.SequenceNumber} appears more than once in the chain.");
            }

            if (current.SequenceNumber != expectedSequenceNumber)
            {
                return ChainVerificationResult.Invalid(
                    verifiedCount,
                    current.EventId,
                    current.SequenceNumber,
                    ChainViolationType.SequenceGap,
                    $"Expected sequence number {expectedSequenceNumber} but found {current.SequenceNumber}.");
            }

            if (previous is null)
            {
                if (!string.Equals(current.PreviousHash, HashChain.GenesisHash, StringComparison.Ordinal))
                {
                    return ChainVerificationResult.Invalid(
                        verifiedCount,
                        current.EventId,
                        current.SequenceNumber,
                        ChainViolationType.InvalidGenesisRelationship,
                        $"The first record in the chain must have PreviousHash = {HashChain.GenesisHash}.");
                }
            }
            else if (!string.Equals(current.PreviousHash, previous.ContentHash, StringComparison.Ordinal))
            {
                return ChainVerificationResult.Invalid(
                    verifiedCount,
                    current.EventId,
                    current.SequenceNumber,
                    ChainViolationType.PreviousHashMismatch,
                    $"PreviousHash does not match the content hash of sequence number {previous.SequenceNumber}.");
            }

            if (!current.HasValidContentHash(hasher))
            {
                return ChainVerificationResult.Invalid(
                    verifiedCount,
                    current.EventId,
                    current.SequenceNumber,
                    ChainViolationType.ContentHashMismatch,
                    $"ContentHash does not match the recomputed canonical hash for sequence number {current.SequenceNumber}.");
            }

            previous = current;
            expectedSequenceNumber++;
            verifiedCount++;
        }

        return ChainVerificationResult.Valid(verifiedCount);
    }
}
