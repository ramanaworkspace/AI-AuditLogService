using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditLogService.Api.Contracts;

namespace AuditLogService.LoadTests;

public sealed record AppendAttempt(string Token, bool Succeeded, double LatencyMilliseconds,
    int? StatusCode, string? Error, AuditEventResponse? Receipt);

public sealed record LoadTestResult(
    string RunId, DateTimeOffset StartedAtUtc, string ApiUrl, int ParallelClients, int WritesPerClient,
    int TimeoutSeconds, double ElapsedSeconds, int BaselineEvents, int FinalEvents,
    IReadOnlyList<AppendAttempt> Attempts, ChainVerificationResponse? FinalVerification,
    IReadOnlyList<string> ValidationErrors)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public int PlannedRequests => ParallelClients * WritesPerClient;
    public int TotalRequests => Attempts.Count;
    public int SuccessfulWrites => Attempts.Count(attempt => attempt.Succeeded);
    public int FailedWrites => TotalRequests - SuccessfulWrites;
    public double? ThroughputWritesPerSecond => ElapsedSeconds > 0 ? SuccessfulWrites / ElapsedSeconds : null;
    public double? P95AppendLatencyMilliseconds => Percentile(
        Attempts.Where(attempt => attempt.Succeeded).Select(attempt => attempt.LatencyMilliseconds));
    public double? P95AllRequestsLatencyMilliseconds => Percentile(Attempts.Select(attempt => attempt.LatencyMilliseconds));
    public bool IsSuccessful => TotalRequests == PlannedRequests && FailedWrites == 0
        && ValidationErrors.Count == 0 && FinalVerification?.IsValid == true;

    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

    public string ToMarkdown()
    {
        static string Number(double? value) => value?.ToString("F3", CultureInfo.InvariantCulture) ?? "N/A";
        var text = new StringBuilder();
        text.AppendLine("# Scenario D local load-test result");
        text.AppendLine(CultureInfo.InvariantCulture, $"\nRun: `{RunId}`; UTC start: `{StartedAtUtc:O}`; API: `{ApiUrl}`");
        text.AppendLine("\n| Measurement | Actual result |\n|---|---|");
        text.AppendLine(CultureInfo.InvariantCulture, $"| Parallel clients / writes per client | {ParallelClients} / {WritesPerClient} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| Planned / total requests | {PlannedRequests} / {TotalRequests} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| Successful / failed writes | {SuccessfulWrites} / {FailedWrites} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| Elapsed append workload (seconds) | {Number(ElapsedSeconds)} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| Successful writes/second | {Number(ThroughputWritesPerSecond)} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| p95 successful append latency (ms) | {Number(P95AppendLatencyMilliseconds)} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| p95 all request latency (ms) | {Number(P95AllRequestsLatencyMilliseconds)} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| Baseline / final stored events | {BaselineEvents} / {FinalEvents} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| Final chain valid | {FinalVerification?.IsValid.ToString() ?? "Unavailable"} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| Overall passed | {IsSuccessful} |");
        foreach (var error in ValidationErrors)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- Validation error: {error}");
        }

        foreach (var group in Attempts.Where(attempt => !attempt.Succeeded).GroupBy(attempt => attempt.Error))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- Failed requests: {group.Count()} ({group.Key})");
        }

        text.AppendLine("\nNearest-rank p95; successful latency includes HTTP response/receipt parsing. No retries.");
        text.AppendLine("Elapsed time excludes baseline/final queries and verification. This local run is not a production performance claim.");
        return text.ToString();
    }

    private static double? Percentile(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        return ordered.Length == 0 ? null : ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
    }
}
