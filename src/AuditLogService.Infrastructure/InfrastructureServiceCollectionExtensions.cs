using AuditLogService.Application.Append;
using AuditLogService.Application.Query;
using AuditLogService.Application.Verification;
using AuditLogService.Domain;
using AuditLogService.Infrastructure.Append;
using AuditLogService.Infrastructure.Persistence;
using AuditLogService.Infrastructure.Query;
using AuditLogService.Infrastructure.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuditLogService.Infrastructure;

/// <summary>
/// Registers the PostgreSQL-backed audit log persistence, append, query, and verification
/// services.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AuditLogDbContext"/> (as a pooled context factory),
    /// the canonical hashing services, <see cref="IAuditEventAppendService"/>,
    /// <see cref="IAuditEventQueryService"/>, and <see cref="IChainVerificationService"/>.
    /// </summary>
    /// <remarks>
    /// The connection string is intentionally resolved lazily from <see cref="IConfiguration"/>
    /// inside the <see cref="IDbContextFactory{TContext}"/> options callback - i.e. the first time
    /// the factory is actually used - rather than being read eagerly up front. Reading it eagerly
    /// (e.g. via <c>builder.Configuration.GetConnectionString(...)</c> before <c>builder.Build()</c>
    /// in a minimal-hosting <c>Program.cs</c>) can observe configuration from before
    /// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> test
    /// overrides (e.g. <c>ConfigureAppConfiguration</c>) have been applied, silently connecting
    /// integration tests to the wrong database.
    /// </remarks>
    public static IServiceCollection AddAuditLogInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPooledDbContextFactory<AuditLogDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>()
                .GetConnectionString("AuditLogDatabase")
                ?? throw new InvalidOperationException(
                    "Missing required configuration value 'ConnectionStrings:AuditLogDatabase'.");
            options.UseNpgsql(connectionString);
        });
        services.AddSingleton<ICanonicalEventSerializer, CanonicalEventSerializer>();
        services.AddSingleton<IEventHasher, Sha256EventHasher>();
        services.AddScoped<IAuditEventAppendService, PostgresAuditEventAppendService>();
        services.AddScoped<IAuditEventQueryService, PostgresAuditEventQueryService>();
        services.AddScoped<IChainVerificationService, PostgresChainVerificationService>();

        return services;
    }
}
