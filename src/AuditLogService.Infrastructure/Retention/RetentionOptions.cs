namespace AuditLogService.Infrastructure.Retention;

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public TimeSpan Window { get; set; } = TimeSpan.FromDays(90);
}
