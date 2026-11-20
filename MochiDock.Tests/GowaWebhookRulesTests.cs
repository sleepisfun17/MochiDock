using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class GowaWebhookRulesTests
{
    [Theory]
    [InlineData("http://100.64.0.2:47823/webhook?token=abc123")]
    [InlineData("http://192.168.1.20:47823/webhook")]
    [InlineData("HTTP://localhost:47823/webhook?token=x")]
    public void ValidTargetsAreAccepted(string url) => Assert.True(WebhookRelayRules.IsValidTarget(url));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://host/webhook")]
    [InlineData("ftp://host")]
    [InlineData("http://host/a b")]
    [InlineData("http://host/a,b")]
    [InlineData("http://host/\"quoted\"")]
    [InlineData("not a url")]
    public void InvalidTargetsAreRejected(string url) => Assert.False(WebhookRelayRules.IsValidTarget(url));

    [Fact]
    public void ParseTargetsSkipsBlanksCommentsInvalidLinesAndDuplicates()
    {
        var targets = WebhookRelayRules.ParseTargets(new[]
        {
            "# docks", "", "  http://100.1.1.1:47823/webhook?token=aaa  ", "http://100.1.1.1:47823/webhook?token=aaa",
            "https://secure/webhook?token=bbb", "http://100.2.2.2:47823/webhook?token=ccc", "junk",
        }, out var rejected);
        Assert.Equal(new[] { "http://100.1.1.1:47823/webhook?token=aaa", "http://100.2.2.2:47823/webhook?token=ccc" }, targets);
        Assert.Equal(2, rejected.Count);
        Assert.All(rejected, r => Assert.DoesNotContain("bbb", r)); // a rejected line is reported without its token
    }

    [Theory]
    [InlineData("http://h:1/webhook?token=secret", "http://h:1/webhook?token=***")]
    [InlineData("http://h:1/webhook?x=1&token=secret&y=2", "http://h:1/webhook?x=1&token=***&y=2")]
    [InlineData("http://h:1/webhook?TOKEN=secret", "http://h:1/webhook?TOKEN=***")]
    [InlineData("http://h:1/webhook", "http://h:1/webhook")]
    [InlineData("", "")]
    public void RedactTokenHidesTheSecretOnly(string input, string expected) => Assert.Equal(expected, WebhookRelayRules.RedactToken(input));

    [Fact]
    public void RedactTokenHandlesNull() => Assert.Equal("", WebhookRelayRules.RedactToken(null));

    [Theory]
    [InlineData("http://100.64.0.2:47823/webhook?token=secret", "100.64.0.2:47823")]
    [InlineData("http://example.test/webhook?token=secret", "example.test")]
    [InlineData("garbage", "(invalid address)")]
    public void DescribeIsHostAndPortWithoutAnyToken(string url, string expected)
    {
        var text = WebhookRelayRules.Describe(url);
        Assert.Equal(expected, text);
        Assert.DoesNotContain("secret", text);
    }

    [Theory]
    [InlineData("Content-Type", true)]
    [InlineData("X-Hub-Signature-256", true)]
    [InlineData("Host", false)]
    [InlineData("content-length", false)]
    [InlineData("Connection", false)]
    [InlineData("Transfer-Encoding", false)]
    [InlineData("", false)]
    public void OnlyPerRequestHeadersAreForwarded(string name, bool expected) => Assert.Equal(expected, WebhookRelayRules.ShouldForwardHeader(name));
}

public class AckReceiptPolicyTests
{
    [Theory]
    [InlineData("read", null, true, ReceiptDismissal.ByIds)]        // GOWA's usual read receipt: only the notifications whose ids it names
    [InlineData("read", false, true, ReceiptDismissal.ByIds)]
    [InlineData("READ", null, true, ReceiptDismissal.ByIds)]
    [InlineData("read", null, false, ReceiptDismissal.None)]       // without ids a plain read never clears, and never a whole chat
    [InlineData("read", false, false, ReceiptDismissal.None)]
    [InlineData("read", true, true, ReceiptDismissal.None)]        // the payload says the message was yours
    [InlineData("read-self", null, true, ReceiptDismissal.ByIds)]
    [InlineData("read-self", null, false, ReceiptDismissal.WholeChat)]
    [InlineData("READ-SELF", false, false, ReceiptDismissal.WholeChat)]
    [InlineData("read-self", true, false, ReceiptDismissal.None)]
    [InlineData("delivered", null, true, ReceiptDismissal.None)]
    [InlineData("played", false, false, ReceiptDismissal.None)]
    [InlineData("", null, true, ReceiptDismissal.None)]
    [InlineData(null, null, false, ReceiptDismissal.None)]
    public void ReadReceiptsClearByIdsAndOnlyReadSelfClearsAWholeChat(string? type, bool? fromMe, bool hasIds, ReceiptDismissal expected) =>
        Assert.Equal(expected, AckReceiptPolicy.Decide(type, fromMe, hasIds));

    [Theory]
    [InlineData("read", true)][InlineData("read-self", true)][InlineData("delivered", false)][InlineData(null, false)]
    public void ReadReceiptsProveTheReadSyncPath(string? type, bool expected) => Assert.Equal(expected, AckReceiptPolicy.IsReadReceipt(type));
}

public class ReadSyncStatusTextTests
{
    static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShowsTheLastReadReceiptWithItsAge()
    {
        var text = ReadSyncStatusText.Build(Now.AddMinutes(-3), Now.AddMinutes(-1), 0, Now);
        Assert.StartsWith("Last read receipt: ", text);
        Assert.Contains("(3 min ago)", text);
        Assert.DoesNotContain("rejected", text);
    }

    [Theory]
    [InlineData(20, "just now")][InlineData(59 * 60, "59 min ago")][InlineData(3 * 3600, "3 h ago")][InlineData(2 * 86400, "2 d ago")]
    public void AgeIsReadable(int seconds, string expected) =>
        Assert.Contains(expected, ReadSyncStatusText.Build(Now.AddSeconds(-seconds), null, 0, Now));

    [Fact]
    public void SaysSoWhenNothingArrivedOrOnlyOtherReceipts()
    {
        Assert.Equal("No read receipt received since MochiDock started.", ReadSyncStatusText.Build(null, null, 0, Now));
        Assert.Contains("no read receipt yet", ReadSyncStatusText.Build(null, Now.AddMinutes(-2), 0, Now));
    }

    [Fact]
    public void ReportsRejectedCallsWithTheFix()
    {
        var text = ReadSyncStatusText.Build(null, null, 3, Now);
        Assert.Contains("3 calls rejected", text);
        Assert.Contains("token mismatch", text);
        Assert.Contains("1 call rejected", ReadSyncStatusText.Build(null, null, 1, Now));
    }
}
