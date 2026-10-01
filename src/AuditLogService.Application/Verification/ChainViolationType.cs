namespace AuditLogService.Application.Verification;

/// <summary>
/// Categorizes the kind of hash-chain inconsistency detected by
/// <see cref="IChainVerificationService"/>.
/// </summary>
public enum ChainViolationType
{
    /// <summary>
    /// An event's <c>ContentHash</c> does not match the canonical hash recomputed from its
    /// own fields.
    /// </summary>
    ContentHashMismatch,

    /// <summary>
    /// An event's <c>PreviousHash</c> does not equal the <c>ContentHash</c> of the
    /// immediately preceding event in sequence order.
    /// </summary>
    PreviousHashMismatch,

    /// <summary>A sequence number is missing between two consecutive events.</summary>
    SequenceGap,

    /// <summary>The same sequence number appears on more than one event.</summary>
    DuplicateSequenceNumber,

    /// <summary>
    /// The first event in the chain (sequence number 1) does not have
    /// <c>PreviousHash = GENESIS</c>.
    /// </summary>
    InvalidGenesisRelationship
}
