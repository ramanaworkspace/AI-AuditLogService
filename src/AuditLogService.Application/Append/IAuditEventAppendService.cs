using AuditLogService.Domain;

namespace AuditLogService.Application.Append;

/// <summary>
/// Appends a new audit event to the single, globally ordered hash chain.
/// </summary>
/// <remarks>
/// Implementations must guarantee that the append is atomic: sequence-number
/// assignment, previous-hash resolution, content-hash computation, event insertion, and
/// chain-head update either all succeed together or none take effect.
/// </remarks>
public interface IAuditEventAppendService
{
    /// <summary>
    /// Assigns the next sequence number and previous hash, constructs and hashes the
    /// event, persists it, and advances the chain head - all within a single atomic
    /// operation.
    /// </summary>
    /// <param name="request">The descriptive content of the event to append.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The persisted, hashed audit event.</returns>
    Task<AuditEvent> AppendAsync(AppendAuditEventRequest request, CancellationToken cancellationToken = default);
}
