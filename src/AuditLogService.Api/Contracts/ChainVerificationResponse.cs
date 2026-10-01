using AuditLogService.Application.Verification;

namespace AuditLogService.Api.Contracts;

/// <summary>
/// Response body for <c>GET /api/v1/audit-events/verify</c>.
/// </summary>
public sealed record ChainVerificationResponse(
    bool IsValid,
    long EventsVerified,
    Guid? FirstInconsistentEventId,
    long? FirstInconsistentSequenceNumber,
    ChainViolationType? ViolationType,
    string? Detail)
{
    public static ChainVerificationResponse FromResult(ChainVerificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ChainVerificationResponse(
            result.IsValid,
            result.EventsVerified,
            result.FirstInconsistentEventId,
            result.FirstInconsistentSequenceNumber,
            result.ViolationType,
            result.Detail);
    }
}
