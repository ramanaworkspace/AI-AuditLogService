using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditLogService.Application.Reporting;
using AuditLogService.Domain;

namespace AuditLogService.UnitTests.Reporting;

public sealed class AccountAccessReportTests
{
    [Theory]
    [InlineData(AccountAccessPolicy.Succeeded, "succeeded")]
    [InlineData(AccountAccessPolicy.Denied, "denied")]
    [InlineData(AccountAccessPolicy.Failed, "failed")]
    [InlineData("CLIENT_ACCOUNT_UPDATED", null)]
    [InlineData("client_account_access_succeeded", null)]
    [InlineData("account.read", null)]
    public void TaxonomyIsAnExactAllowlist(string eventType, string? outcome) =>
        Assert.Equal(outcome, AccountAccessPolicy.Outcome(eventType));

    [Fact]
    public void QueryRejectsInvalidRangeAndResourceAndNormalizesOffsets()
    {
        var time = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(5.5));
        Assert.Throws<ArgumentException>(() => new AccountAccessReportQuery(time, time.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => new AccountAccessReportQuery(time, time, "DOCUMENT"));
        var query = new AccountAccessReportQuery(time, time);
        Assert.Equal(TimeSpan.Zero, query.StartTime.Offset);
        Assert.Equal(time.UtcDateTime, query.EndTime.UtcDateTime);
        Assert.Equal("2026-10-03T06:30:00.0000000Z", AccountAccessReport.UtcText(time));
    }

    [Fact]
    public void JsonIsByteDeterministicIncludingNestedPayloadAndCulture()
    {
        static AccountAccessReport Create(string payload) => new(
            "account-access-v1", "CLIENT_ACCOUNT", "2026-10-03T00:00:00.0000000Z",
            "2026-10-03T00:00:00.0000000Z",
            new AccountAccessChainStatus(true, 1, 0, 1, new string('a', 64), null, null, null, null),
            [new AccountAccessReportItem(Guid.Empty, 1, AccountAccessPolicy.Succeeded, "succeeded",
                "actor", "CLIENT_ACCOUNT", "1", JsonSerializer.Deserialize<JsonElement>(payload),
                "2026-10-03T00:00:00.0000000Z", HashChain.GenesisHash, new string('a', 64), false, null)]);
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var first = Create("""{"z":1.00,"a":{"y":null,"x":"é"}}""").ToJsonBytes();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var second = Create("""{"a":{"x":"é","y":null},"z":1}""").ToJsonBytes();
            Assert.Equal(first, second);
            var text = Encoding.UTF8.GetString(first);
            Assert.StartsWith("""{"chainStatus":""", text, StringComparison.Ordinal);
            Assert.Contains("\"violationType\":null", text, StringComparison.Ordinal);
            Assert.Contains("\\u00e9", text, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
