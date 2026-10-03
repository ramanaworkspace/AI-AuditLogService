using System.Text.Json;
using AuditLogService.Api.Contracts;
using AuditLogService.Application.Append;
using AuditLogService.Application.Query;
using AuditLogService.Application.Redaction;
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
    private static readonly Action<ILogger, Exception?> LogInvalidPayload = LoggerMessage.Define(
        LogLevel.Warning, new EventId(1001, "InvalidAuditPayload"), "Rejected invalid audit payload.");

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
        IPayloadProtector payloadProtector,
        ILoggerFactory loggerFactory,
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

        AuditLogService.Domain.AuditEvent appended;
        try
        {
            appended = await appendService.AppendAsync(
                new AppendAuditEventRequest(
                    request.EventType!,
                    request.ActorId!,
                    request.ResourceType!,
                    request.ResourceId!,
                    request.Payload),
                cancellationToken);
        }
        catch (PayloadProtectionException)
        {
            LogInvalidPayload(loggerFactory.CreateLogger("AuditEventValidation"), null);
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["payload"] = ["Payload contains invalid or reserved JSON properties."] });
        }

        return Results.Created(
            $"/api/v1/audit-events/{appended.EventId}",
            AuditEventResponse.FromDomain(appended, readPayload: payloadProtector.Project(appended.Payload)));
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
                result.Items.Select(item => AuditEventResponse.FromDomain(
                    item, result.ArchivedAtByEventId.TryGetValue(item.EventId, out var archivedAt)
                        ? archivedAt : null, result.ReadPayloadByEventId[item.EventId])).ToList(),
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
