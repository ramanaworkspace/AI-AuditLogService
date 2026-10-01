using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AuditLogService.Infrastructure.Persistence;

public sealed class AuditLogDbContextFactory : IDesignTimeDbContextFactory<AuditLogDbContext>
{
    public AuditLogDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUDITLOG_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=audit_log;Username=audit_log;Password=audit_log_development";
        var options = new DbContextOptionsBuilder<AuditLogDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AuditLogDbContext(options);
    }
}
