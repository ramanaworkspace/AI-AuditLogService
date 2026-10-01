namespace AuditLogService.Application.Verification;

/// <summary>
/// The outcome of walking the audit event hash chain end to end.
/// </summary>
/// <remarks>
/// Verification stops at the first inconsistency it finds. <see cref="EventsVerified"/>
/// reports how many events, starting from sequence number 1, were confirmed consistent
/// before that point (or the total chain length when <see cref="IsValid"/> is
/// <see langword="true"/>).
/// </remarks>
public sealed record ChainVerificationResult(
    bool IsValid,
    long EventsVerified,
    Guid? FirstInconsistentEventId,
    long? FirstInconsistentSequenceNumber,
    ChainViolationType? ViolationType,
    string? Detail)
{
    /// <summary>Creates a result reporting that the entire chain is internally consistent.</summary>
    public static ChainVerificationResult Valid(long eventsVerified) =>
        new(true, eventsVerified, null, null, null, null);

    /// <summary>Creates a result reporting the first inconsistency found while walking the chain.</summary>
    public static ChainVerificationResult Invalid(
        long eventsVerified,
        Guid firstInconsistentEventId,
        long firstInconsistentSequenceNumber,
        ChainViolationType violationType,
        string detail) =>
        new(false, eventsVerified, firstInconsistentEventId, firstInconsistentSequenceNumber, violationType, detail);
}
