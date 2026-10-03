using System.Globalization;
using AuditLogService.Application.Reporting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AuditLogService.Api.Endpoints;

public static class AccountAccessReportEndpoints
{
    public static IEndpointRouteBuilder MapAccountAccessReport(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/reports/account-access", CreateAsync)
            .WithName("GetAccountAccessReport")
            .WithSummary("Returns a redacted prototype CLIENT_ACCOUNT access report.")
            .WithDescription("Inclusive explicit-offset timestamps; exact access-event allowlist; global snapshot chain status. Not regulatory certification.")
            .Produces<AccountAccessReport>(StatusCodes.Status200OK, "application/json")
            .ProducesValidationProblem();
        return endpoints;
    }

    private static async Task<Results<FileContentHttpResult, ValidationProblem>> CreateAsync(
        string? startTime, string? endTime, string? resourceType,
        IAccountAccessReportService service, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var startValid = TryInstant(startTime, out var start);
        var endValid = TryInstant(endTime, out var end);
        if (!startValid)
        {
            errors["startTime"] = ["An ISO 8601 timestamp with Z or an explicit offset is required."];
        }

        if (!endValid)
        {
            errors["endTime"] = ["An ISO 8601 timestamp with Z or an explicit offset is required."];
        }

        if (startValid && endValid && start > end)
        {
            errors["endTime"] = ["endTime must be on or after startTime."];
        }

        if (resourceType is not null && resourceType != AccountAccessPolicy.ResourceType)
        {
            errors["resourceType"] = ["Only CLIENT_ACCOUNT is supported."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var report = await service.CreateAsync(new AccountAccessReportQuery(start, end), cancellationToken);
        return TypedResults.Bytes(report.ToJsonBytes(), "application/json; charset=utf-8");
    }

    private static bool TryInstant(string? text, out DateTimeOffset instant) =>
        DateTimeOffset.TryParseExact(text,
            ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out instant);
}
