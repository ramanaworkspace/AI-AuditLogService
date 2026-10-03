namespace AuditLogService.Application.Redaction;

public sealed class PayloadProtectionException : Exception
{
    public PayloadProtectionException() : base("Payload contains invalid or reserved JSON properties.")
    {
    }
}
