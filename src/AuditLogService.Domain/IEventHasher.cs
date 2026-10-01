namespace AuditLogService.Domain;

public interface IEventHasher
{
    string ComputeHash(AuditEventData eventData);
}