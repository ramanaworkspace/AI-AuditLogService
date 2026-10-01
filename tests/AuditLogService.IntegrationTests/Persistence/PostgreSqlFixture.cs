using AuditLogService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditLogService.IntegrationTests.Persistence;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5433;Database=audit_log_test;Username=audit_log;Password=audit_log_development";

    public PostgreSqlFixture()
    {
        ConnectionString = Environment.GetEnvironmentVariable("AUDITLOG_TEST_CONNECTION_STRING")
            ?? DefaultConnectionString;
    }

    public string ConnectionString { get; }

    public AuditLogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditLogDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AuditLogDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task ResetAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE audit_events");
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE chain_metadata SET head_sequence_number = 0, head_hash = 'GENESIS' WHERE chain_id = 1");
    }
}
