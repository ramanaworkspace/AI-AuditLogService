using AuditLogService.Domain;

namespace AuditLogService.Application.Query;

/// <summary>
/// A page of audit events ordered ascending by <see cref="AuditEvent.SequenceNumber"/>.
/// </summary>
/// <param name="Items">The events in this page, in ascending sequence-number order.</param>
/// <param name="NextCursor">
/// An opaque token to pass as the next request's cursor to fetch the following page, or
/// <see langword="null"/> when there are no more events after this page.
/// </param>
public sealed record AuditEventQueryResult(IReadOnlyList<AuditEvent> Items, string? NextCursor)
{
    public IReadOnlyDictionary<Guid, DateTimeOffset> ArchivedAtByEventId { get; init; } =
        new Dictionary<Guid, DateTimeOffset>();
}
