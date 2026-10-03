using AuditLogService.LoadTests;

namespace AuditLogService.UnitTests.LoadTesting;

public sealed class LoadTestResultTests
{
    [Fact]
    public void MetricsUseActualAttemptsAndNearestRankSuccessfulLatency()
    {
        var attempts = Enumerable.Range(1, 20)
            .Select(index => new AppendAttempt(index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                true, index, 201, null, null)).ToList();
        attempts.Add(new AppendAttempt("failed", false, 500, 500, "HTTP failure", null));
        var result = new LoadTestResult("test", DateTimeOffset.UnixEpoch, "http://localhost:5115",
            50, 1, 180, 2, 0, 20, attempts, null, []);
        Assert.Equal(21, result.TotalRequests);
        Assert.Equal(20, result.SuccessfulWrites);
        Assert.Equal(1, result.FailedWrites);
        Assert.Equal(10, result.ThroughputWritesPerSecond);
        Assert.Equal(19, result.P95AppendLatencyMilliseconds);
        Assert.Equal(20, result.P95AllRequestsLatencyMilliseconds);
        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public void MissingMeasurementsAreNullRatherThanInventedZeros()
    {
        var result = new LoadTestResult("test", DateTimeOffset.UnixEpoch, "http://localhost:5115",
            50, 1, 180, 0, 0, 0, [], null, ["API unavailable"]);
        Assert.Null(result.ThroughputWritesPerSecond);
        Assert.Null(result.P95AppendLatencyMilliseconds);
        Assert.Null(result.P95AllRequestsLatencyMilliseconds);
        Assert.False(result.IsSuccessful);
        Assert.Contains("N/A", result.ToMarkdown(), StringComparison.Ordinal);
        Assert.Contains("API unavailable", result.ToMarkdown(), StringComparison.Ordinal);
    }
}
