using AuditLogService.Api.Endpoints;
using AuditLogService.Infrastructure;
using AuditLogService.Application.Redaction;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
    options.ThrowOnBadRequest = false);

builder.Services.AddAuditLogInfrastructure(builder.Configuration);

var app = builder.Build();
// Validate path policy before accepting requests, not after sensitive input arrives.
_ = app.Services.GetRequiredService<IPayloadProtector>();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapAuditEventEndpoints();
app.MapAccountAccessReport();

app.Run();

/// <summary>
/// Exposes the generated top-level program type to integration tests.
/// </summary>
public partial class Program;
