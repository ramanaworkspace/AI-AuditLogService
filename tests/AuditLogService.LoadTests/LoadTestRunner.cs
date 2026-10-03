using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using AuditLogService.Api.Contracts;
using AuditLogService.Domain;

namespace AuditLogService.LoadTests;

public static class LoadTestRunner
{
    public static async Task<LoadTestResult> RunAsync(Uri apiUrl, int clients, int writesPerClient, int timeoutSeconds)
    {
        var runId = Guid.NewGuid().ToString("N");
        var started = DateTimeOffset.UtcNow;
        var errors = new List<string>();
        using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = clients };
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(apiUrl.AbsoluteUri.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
        var attempts = new List<AppendAttempt>();
        List<AuditEventResponse> baseline = [];
        List<AuditEventResponse> final = [];
        ChainVerificationResponse? verification = null;
        var elapsed = TimeSpan.Zero;
        try
        {
            baseline = await ReadAllAsync(http);
            var initialVerification = await http.GetFromJsonAsync<ChainVerificationResponse>("api/v1/audit-events/verify");
            if (initialVerification is null || !initialVerification.IsValid
                || initialVerification.EventsVerified != baseline.Count)
            {
                throw new InvalidOperationException("Baseline chain is invalid or changed during baseline capture. Use a quiet dedicated API/database.");
            }

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, clients).Select(async client =>
            {
                await gate.Task;
                var local = new List<AppendAttempt>();
                for (var write = 0; write < writesPerClient; write++)
                {
                    var token = $"{runId}-{client}-{write}";
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        using var response = await http.PostAsJsonAsync("api/v1/audit-events", new
                        {
                            eventType = "LOAD_TEST_APPEND",
                            actorId = runId,
                            resourceType = "LOAD_TEST",
                            resourceId = token,
                            payload = new { runId, client, write }
                        });
                        if (response.StatusCode != System.Net.HttpStatusCode.Created)
                        {
                            local.Add(new AppendAttempt(token, false, watch.Elapsed.TotalMilliseconds,
                                (int)response.StatusCode, "HTTP response was not 201 Created.", null));
                            continue;
                        }

                        var receipt = await response.Content.ReadFromJsonAsync<AuditEventResponse>();
                        local.Add(new AppendAttempt(token, receipt is not null, watch.Elapsed.TotalMilliseconds,
                            (int)response.StatusCode, receipt is null ? "Missing append receipt." : null, receipt));
                    }
                    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
                    {
                        local.Add(new AppendAttempt(token, false, watch.Elapsed.TotalMilliseconds,
                            null, exception.GetType().Name, null));
                    }
                }

                return local;
            }).ToArray();
            var clock = Stopwatch.StartNew();
            gate.SetResult();
            var completed = await Task.WhenAll(tasks);
            clock.Stop();
            elapsed = clock.Elapsed;
            attempts = completed.SelectMany(items => items).ToList();
            final = await ReadAllAsync(http);
            verification = await http.GetFromJsonAsync<ChainVerificationResponse>("api/v1/audit-events/verify");
            Validate(baseline, final, attempts, runId, verification, errors);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            or JsonException or InvalidOperationException)
        {
            errors.Add($"Run/validation failed: {exception.GetType().Name}: {exception.Message}");
        }

        return new LoadTestResult(runId, started, apiUrl.AbsoluteUri, clients, writesPerClient, timeoutSeconds,
            elapsed.TotalSeconds, baseline.Count, final.Count, attempts, verification, errors);
    }

    private static async Task<List<AuditEventResponse>> ReadAllAsync(HttpClient http)
    {
        var rows = new List<AuditEventResponse>();
        string? cursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var url = "api/v1/audit-events?limit=500" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var page = await http.GetFromJsonAsync<AuditEventListResponse>(url)
                ?? throw new InvalidOperationException("Missing query response.");
            rows.AddRange(page.Items);
            cursor = page.NextCursor;
            if (cursor is not null && !seen.Add(cursor))
            {
                throw new InvalidOperationException("Pagination cursor repeated.");
            }
        } while (cursor is not null);

        return rows.OrderBy(row => row.SequenceNumber).ToList();
    }

    private static void Validate(List<AuditEventResponse> baseline, List<AuditEventResponse> final,
        List<AppendAttempt> attempts, string runId, ChainVerificationResponse? verification, List<string> errors)
    {
        if (final.Select(row => row.EventId).Distinct().Count() != final.Count)
        {
            errors.Add("Duplicate EventId detected.");
        }

        if (final.Select(row => row.SequenceNumber).Distinct().Count() != final.Count)
        {
            errors.Add("Duplicate SequenceNumber detected.");
        }

        if (final.Select(row => row.PreviousHash).Distinct(StringComparer.Ordinal).Count() != final.Count)
        {
            errors.Add("Duplicate predecessor detected.");
        }

        var previous = HashChain.GenesisHash;
        for (var index = 0; index < final.Count; index++)
        {
            if (final[index].SequenceNumber != index + 1L)
            {
                errors.Add($"Sequence continuity failure at ordered index {index}.");
            }

            if (final[index].PreviousHash != previous)
            {
                errors.Add($"Predecessor mismatch at sequence {final[index].SequenceNumber}.");
            }

            previous = final[index].ContentHash;
        }

        foreach (var original in baseline)
        {
            if (!final.Any(row => row.EventId == original.EventId && row.ContentHash == original.ContentHash
                && row.SequenceNumber == original.SequenceNumber))
            {
                errors.Add($"Baseline event changed/missing: {original.EventId}.");
            }
        }

        var runRows = final.Where(row => row.ActorId == runId).ToArray();
        var receipts = attempts.Where(attempt => attempt.Succeeded).ToArray();
        if (runRows.Length != receipts.Length || final.Count != baseline.Count + runRows.Length)
        {
            errors.Add("Persisted/acknowledged count mismatch or external writes during run.");
        }

        foreach (var attempt in receipts)
        {
            var matches = runRows.Where(row => row.EventId == attempt.Receipt!.EventId).ToArray();
            if (matches.Length != 1 || matches[0].ResourceId != attempt.Token
                || matches[0].ContentHash != attempt.Receipt!.ContentHash
                || matches[0].SequenceNumber != attempt.Receipt.SequenceNumber)
            {
                errors.Add($"Missing/inconsistent acknowledged write: {attempt.Token}.");
            }
        }

        if (runRows.Select(row => row.ResourceId).Distinct(StringComparer.Ordinal).Count() != runRows.Length)
        {
            errors.Add("Repeated logical request token persisted.");
        }

        if (verification is null || !verification.IsValid || verification.EventsVerified != final.Count)
        {
            errors.Add("Final global-chain verification failed or final snapshot changed.");
        }
    }
}
