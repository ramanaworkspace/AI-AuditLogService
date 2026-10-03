using AuditLogService.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AuditLogService.IntegrationTests.Api;

/// <summary>
/// Hosts the real <c>AuditLogService.Api</c> application (via <c>Program</c>) against the shared
/// integration-test PostgreSQL database, so the Scenario A endpoints can be exercised end to end
/// over HTTP.
/// </summary>
public sealed class AuditEventApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The connection string the hosted application should use. Tests set this to the shared
    /// <see cref="PostgreSqlFixture.ConnectionString"/> before the first request is made.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    public IReadOnlyDictionary<string, string?>? AdditionalConfiguration { get; set; }

    public ILoggerProvider? LogProvider { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuditLogDatabase"] = ConnectionString,
            });
            if (AdditionalConfiguration is not null)
            {
                configBuilder.AddInMemoryCollection(AdditionalConfiguration);
            }
        });
        if (LogProvider is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(LogProvider));
        }
    }
}
