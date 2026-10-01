using AuditLogService.Application.Append;
using AuditLogService.Domain;
using AuditLogService.Infrastructure.Append;
using AuditLogService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuditLogService.Infrastructure;

/// <summary>
/// Registers the PostgreSQL-backed audit log persistence and append services.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AuditLogDbContext"/> (as a pooled context factory),
    /// the canonical hashing services, and <see cref="IAuditEventAppendService"/>.
    /// </summary>
    public static IServiceCollection AddAuditLogInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddPooledDbContextFactory<AuditLogDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<ICanonicalEventSerializer, CanonicalEventSerializer>();
        services.AddSingleton<IEventHasher, Sha256EventHasher>();
        services.AddScoped<IAuditEventAppendService, PostgresAuditEventAppendService>();

        return services;
    }
}
