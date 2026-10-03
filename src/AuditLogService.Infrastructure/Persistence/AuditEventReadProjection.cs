using System.Text.Json;

namespace AuditLogService.Infrastructure.Persistence;

public sealed class AuditEventReadProjection
{
    public Guid EventId { get; set; }

    public JsonElement Payload { get; set; }
}
