using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AuditLogService.Infrastructure.Persistence;

public sealed class AuditLogDbContextFactory : IDesignTimeDbContextFactory<AuditLogDbContext>
{
    public AuditLogDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUDITLOG_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set AUDITLOG_CONNECTION_STRING to the intended database connection string.");
        var options = new DbContextOptionsBuilder<AuditLogDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AuditLogDbContext(options);
    }
}
