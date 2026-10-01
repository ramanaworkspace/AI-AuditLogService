namespace AuditLogService.Application.Verification;

/// <summary>
/// Verifies that the persisted hash chain is internally consistent.
/// </summary>
public interface IChainVerificationService
{
    /// <summary>
    /// Walks the full chain, ordered ascending by sequence number, and reports whether it is
    /// internally consistent.
    /// </summary>
    Task<ChainVerificationResult> VerifyAsync(CancellationToken cancellationToken = default);
}
