using AuditLogService.Api.Endpoints;
using AuditLogService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

builder.Services.AddAuditLogInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapAuditEventEndpoints();

app.Run();

/// <summary>
/// Exposes the generated top-level program type to integration tests.
/// </summary>
public partial class Program;
