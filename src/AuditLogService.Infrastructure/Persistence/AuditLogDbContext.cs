using Microsoft.EntityFrameworkCore;

namespace AuditLogService.Infrastructure.Persistence;

public sealed class AuditLogDbContext(DbContextOptions<AuditLogDbContext> options) : DbContext(options)
{
    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();

    public DbSet<ChainMetadata> ChainMetadata => Set<ChainMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditLogDbContext).Assembly);
    }
}
