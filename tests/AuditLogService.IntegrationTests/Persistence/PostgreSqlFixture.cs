using AuditLogService.Application.Append;
using AuditLogService.Application.Redaction;
using AuditLogService.Application.Retention;
using AuditLogService.Domain;
using AuditLogService.Infrastructure.Append;
using AuditLogService.Infrastructure.Persistence;
using AuditLogService.Infrastructure.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuditLogService.IntegrationTests.Persistence;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly IDbContextFactory<AuditLogDbContext> _dbContextFactory;

    public PostgreSqlFixture()
    {
        ConnectionString = Environment.GetEnvironmentVariable("AUDITLOG_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set AUDITLOG_TEST_CONNECTION_STRING to a disposable dedicated test database; tests reset its data.");
        _dbContextFactory = new SimpleDbContextFactory(BuildOptions());
    }

    public string ConnectionString { get; }

    private DbContextOptions<AuditLogDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<AuditLogDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

    public AuditLogDbContext CreateDbContext() => new(BuildOptions());

    /// <summary>
    /// Creates an append service backed by a pooled context factory, so that each
    /// <see cref="IAuditEventAppendService.AppendAsync"/> call uses its own
    /// <see cref="AuditLogDbContext"/> (and therefore its own PostgreSQL connection) -
    /// required for tests that exercise genuinely concurrent appends.
    /// </summary>
    public IAuditEventAppendService CreateAppendService()
    {
        var serializer = new CanonicalEventSerializer();
        var hasher = new Sha256EventHasher(serializer);
        return new PostgresAuditEventAppendService(_dbContextFactory, hasher, new CommitmentPayloadProtector([]));
    }

    public IAuditEventRetentionService CreateRetentionService(TimeSpan window, TimeProvider clock) =>
        new PostgresAuditEventRetentionService(
            _dbContextFactory, Options.Create(new RetentionOptions { Window = window }), clock);

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task ResetAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE audit_event_read_projections, audit_event_archives, audit_events");
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE chain_metadata SET head_sequence_number = 0, head_hash = 'GENESIS' WHERE chain_id = 1");
    }

    /// <summary>
    /// Minimal <see cref="IDbContextFactory{TContext}"/> implementation that creates a fresh
    /// <see cref="AuditLogDbContext"/> (and therefore a fresh underlying Npgsql connection) per
    /// call, without requiring the EF Core context-pooling package surface.
    /// </summary>
    private sealed class SimpleDbContextFactory(DbContextOptions<AuditLogDbContext> options)
        : IDbContextFactory<AuditLogDbContext>
    {
        public AuditLogDbContext CreateDbContext() => new(options);

        public Task<AuditLogDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
