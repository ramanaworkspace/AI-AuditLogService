namespace AuditLogService.Domain;

public interface ICanonicalEventSerializer
{
    byte[] Serialize(AuditEventData eventData);
}
