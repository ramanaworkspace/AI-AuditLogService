using System.Text.Json;
using AuditLogService.Domain;

namespace AuditLogService.Application.Redaction;

public interface IAuditEventExportProjector
{
    JsonElement Project(AuditEvent auditEvent);
}
