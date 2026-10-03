using System.Text.Json;

namespace AuditLogService.Application.Redaction;

public interface IPayloadProtector
{
    JsonElement Protect(JsonElement payload);

    JsonElement Project(JsonElement committedPayload);
}
