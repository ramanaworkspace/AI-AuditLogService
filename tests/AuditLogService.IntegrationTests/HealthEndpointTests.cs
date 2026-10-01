using Microsoft.AspNetCore.Mvc.Testing;

namespace AuditLogService.IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpointReturnsSuccess()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }
}
