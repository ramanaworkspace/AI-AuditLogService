using System.Text.Json;
using AuditLogService.Api.Contracts;
using AuditLogService.Application.Append;
using AuditLogService.Application.Query;
using AuditLogService.Application.Verification;
using Microsoft.AspNetCore.Mvc;

namespace AuditLogService.Api.Endpoints;

/// <summary>
/// Maps the Scenario A Minimal API endpoints: append, query, and chain verification.
/// </summary>
public static class AuditEventEndpoints
{
    private const int DefaultPageLimit = 50;
    private const int MaxPageLimit = 500;

    /// <summary>
    /// Maps <c>POST /api/v1/audit-events</c>, <c>GET /api/v1/audit-events</c>, and
    /// <c>GET /api/v1/audit-events/verify</c>.
    /// </summary>
    /// <remarks>
    /// There is intentionally no update or delete endpoint: the audit log is append-only.
    /// </remarks>
    public static IEndpointRouteBuilder MapAuditEventEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/audit-events");

        group.MapPost("/", CreateAuditEventAsync);
        group.MapGet("/", QueryAuditEventsAsync);
        group.MapGet("/verify", VerifyChainAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateAuditEventAsync(
        CreateAuditEventRequest request,
        IAuditEventAppendService appendService,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.EventType))
        {
            errors["eventType"] = ["eventType is required."];
        }

        if (string.IsNullOrWhiteSpace(request.ActorId))
        {
            errors["actorId"] = ["actorId is required."];
        }

        if (string.IsNullOrWhiteSpace(request.ResourceType))
        {
            errors["resourceType"] = ["resourceType is required."];
        }

        if (string.IsNullOrWhiteSpace(request.ResourceId))
        {
            errors["resourceId"] = ["resourceId is required."];
        }

        if (request.Payload.ValueKind != JsonValueKind.Object)
        {
            errors["payload"] = ["payload must be a JSON object."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var appended = await appendService.AppendAsync(
            new AppendAuditEventRequest(
                request.EventType!,
                request.ActorId!,
                request.ResourceType!,
                request.ResourceId!,
                request.Payload),
            cancellationToken);

        return Results.Created(
            $"/api/v1/audit-events/{appended.EventId}",
            AuditEventResponse.FromDomain(appended));
    }

    private static async Task<IResult> QueryAuditEventsAsync(
        [AsParameters] AuditEventQueryParameters parameters,
        IAuditEventQueryService queryService,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        long? afterSequenceNumber = null;
        if (parameters.Cursor is not null)
        {
            if (!AuditEventCursor.TryDecode(parameters.Cursor, out var decoded))
            {
                errors["cursor"] = ["cursor is not a valid pagination token."];
            }
            else
            {
                afterSequenceNumber = decoded;
            }
        }

        var limit = parameters.Limit ?? DefaultPageLimit;
        if (parameters.Limit is not null && (parameters.Limit < 1 || parameters.Limit > MaxPageLimit))
        {
            errors["limit"] = [$"limit must be between 1 and {MaxPageLimit}."];
        }

        if (parameters.StartTimestamp is not null
            && parameters.EndTimestamp is not null
            && parameters.StartTimestamp > parameters.EndTimestamp)
        {
            errors["startTimestamp"] = ["startTimestamp must not be after endTimestamp."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await queryService.QueryAsync(
            new AuditEventQuery(
                parameters.ActorId,
                parameters.ResourceType,
                parameters.ResourceId,
                parameters.EventType,
                parameters.StartTimestamp,
                parameters.EndTimestamp,
                afterSequenceNumber,
                limit),
            cancellationToken);

        return Results.Ok(
            new AuditEventListResponse(
                result.Items.Select(AuditEventResponse.FromDomain).ToList(),
                result.NextCursor));
    }

    private static async Task<IResult> VerifyChainAsync(
        IChainVerificationService verificationService,
        CancellationToken cancellationToken)
    {
        var result = await verificationService.VerifyAsync(cancellationToken);
        return Results.Ok(ChainVerificationResponse.FromResult(result));
    }
}
