using System.Text;
using MochiDock.Core;

/// <summary>Readers and helpers added with the rewrite of the AI usage readers (Codex parsing moved into Core).</summary>
public class AiUsageReaderTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T10:00:00Z");

    [Theory]
    [InlineData(5 * 60, "5 hours")]
    [InlineData(60, "1 hour")]
    [InlineData(10, "1 hour")]
    [InlineData(24 * 60, "1 day")]
    [InlineData(7 * 24 * 60, "7 days")]
    public void SpanLabelsNameTheRealLength(int minutes, string expected) =>
        Assert.Equal(expected, UsageLabels.ForSpan(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void CodexOnlineUsageReadsBothWindowsAndPlan()
    {
        const string json = """
            {"plan_type":"pro_lite","rate_limit":{
              "primary_window":{"used_percent":40,"limit_window_seconds":18000,"reset_at":1794405600},
              "secondary_window":{"used_percent":12.5,"limit_window_seconds":604800,"reset_at":1794700000}}}
            """;
        var reading = AiUsageParsing.ReadCodexUsage(json);
        Assert.NotNull(reading);
        Assert.Equal("Pro Lite", reading!.Plan);
        Assert.Equal(new[] { "5 hours", "7 days" }, reading.Gauges.Select(g => g.Label).ToArray());
        Assert.Equal(0.40, reading.Gauges[0].Used, 3);
        Assert.Equal(0.125, reading.Gauges[1].Used, 3);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794405600), reading.Gauges[0].RenewsAt);
    }

    [Fact]
    public void CodexWindowWithoutUsageIsSkippedAndMissingLengthUsesTheSlotDefault()
    {
        var reading = AiUsageParsing.ReadCodexUsage("""{"rate_limit":{"primary_window":{"limit_window_seconds":18000},"secondary_window":{"used_percent":5}}}""");
        Assert.Single(reading!.Gauges);
        Assert.Equal("7 days", reading.Gauges[0].Label);
        Assert.Equal("", reading.Plan); // not reported: no invented plan name
        Assert.Null(AiUsageParsing.ReadCodexUsage("""{"plan_type":"plus"}"""));
        Assert.Null(AiUsageParsing.ReadCodexUsage("garbage"));
    }

    [Fact]
    public void CodexRolloutLineReadsTokenCountEventsOnly()
    {
        const string line = """{"timestamp":"x","type":"event_msg","payload":{"type":"token_count","rate_limits":{"plan_type":"plus","primary":{"used_percent":72,"window_minutes":300,"resets_at":1794405600},"secondary":{"used_percent":30,"window_minutes":10080}}}}""";
        var reading = AiUsageParsing.ReadCodexRolloutLine(line);
        Assert.NotNull(reading);
        Assert.Equal("Plus", reading!.Plan);
        Assert.Equal(new[] { "5 hours", "7 days" }, reading.Gauges.Select(g => g.Label).ToArray());
        Assert.Equal(0.72, reading.Gauges[0].Used, 3);
        Assert.Null(reading.Gauges[1].RenewsAt);

        Assert.Null(AiUsageParsing.ReadCodexRolloutLine("""{"type":"response_item","payload":{"type":"token_count","rate_limits":{}}}"""));
        Assert.Null(AiUsageParsing.ReadCodexRolloutLine("""{"type":"event_msg","payload":{"type":"agent_message"}}"""));
        Assert.Null(AiUsageParsing.ReadCodexRolloutLine("""ate_limits":{"primary":{"used_percent":1}}}}""")); // cut-off first line of a tail
    }

    [Theory]
    [InlineData("free_workspace", "Free")]
    [InlineData("promax", "Pro Max")]
    [InlineData("team", "Team")]
    [InlineData("enterprise", "Enterprise")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    public void CodexPlanNames(string? id, string expected) => Assert.Equal(expected, AiUsageParsing.CodexPlanName(id));

    [Fact]
    public void ClaudeAppSampleCanDropTheFiveHourFigure()
    {
        var sample = new ClaudeAppSample(Now, 0.3, 0.1);
        Assert.Equal(new[] { "7 days" }, sample.Gauges(includeFiveHour: false).Select(g => g.Label).ToArray());
        Assert.Equal(2, sample.Gauges().Count);
        Assert.Empty(new ClaudeAppSample(Now, 0.3, null).Gauges(includeFiveHour: false));
    }

    [Fact]
    public void ClaudeReadsOpusAndSonnetLimitsWithTheirOwnLabels()
    {
        var gauges = AiUsageParsing.ReadClaudeLimits("""{"seven_day_opus":{"utilization":"50"},"seven_day_sonnet":{"utilization":20,"resets_at":1794405600000}}""");
        Assert.Equal(new[] { UsageLabels.OpusSevenDays, UsageLabels.SonnetSevenDays }, gauges.Select(g => g.Label).ToArray());
        Assert.Equal(0.5, gauges[0].Used, 3);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1794405600000), gauges[1].RenewsAt); // milliseconds are recognised
    }

    [Fact]
    public void NonNumericTextIsNotReadAsZero()
    {
        Assert.Empty(AiUsageParsing.ReadClaudeLimits("""{"five_hour":{"utilization":"n/a"}}"""));
    }

    [Theory]
    [InlineData("abc.DEF-123_x", true)]
    [InlineData("abc def", false)]
    [InlineData("a;b", false)]
    [InlineData("", false)]
    public void CookieSafeTokens(string token, bool expected) => Assert.Equal(expected, AiUsageParsing.IsCookieSafeToken(token));

    [Fact]
    public void Base64UrlDecodesWithoutPaddingAndRejectsJunk()
    {
        var raw = new byte[] { 0xfb, 0xff, 0xfe, 0x01 };
        string url = Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(raw, AiUsageParsing.DecodeBase64Url(url));
        Assert.Null(AiUsageParsing.DecodeBase64Url("***"));
    }

    [Fact]
    public void CursorSessionWithoutProviderPrefixUsesTheWholeSubject()
    {
        string jwt = "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"sub":"user_9","exp":1795000000}""")).TrimEnd('=') + ".sig";
        Assert.Equal("user_9", AiUsageParsing.ReadCursorSession(jwt)!.UserId);
    }

    [Fact]
    public void CursorOnlyReportsPartsThatWereSent()
    {
        var reading = AiUsageParsing.ReadCursorSummary("""{"membershipType":"pro","individualUsage":{"plan":{"apiPercentUsed":40}}}""");
        Assert.Equal(new[] { UsageLabels.CursorMonth, UsageLabels.CursorApiMonth }, reading!.Gauges.Select(g => g.Label).ToArray());
        Assert.Equal(0.40, reading.Gauges[0].Used, 3);
        Assert.Empty(AiUsageParsing.ReadCursorSummary("""{"membershipType":"free"}""")!.Gauges);
    }

    [Fact]
    public void HeadroomIsTheLeftShareOfTheMostSpentLimit()
    {
        Assert.Null(AiUsageParsing.Headroom(Array.Empty<double>()));
        Assert.Equal(12.3, AiUsageParsing.Headroom(new[] { 0.2, 0.877 }));
        Assert.Equal(0.0, AiUsageParsing.Headroom(new[] { 1.4 }));
    }

    [Fact]
    public void AntigravityLiveCanLeaveOutThePartnerPool()
    {
        const string json = """
        {"userStatus":{"cascadeModelConfigData":{"clientModelConfigs":[
          {"label":"GPT-OSS 120B (Medium)","quotaInfo":{"remainingFraction":0.1}},
          {"label":"Gemini 3.1 Pro (High)","quotaInfo":{"remainingFraction":0.6}}]}}}
        """;
        var live = AiUsageParsing.ReadAntigravityLive(json, Now, includePartnerPool: false);
        Assert.Equal("", live!.Plan);
        Assert.Equal(new[] { "Gemini Pro" }, live.Gauges.Select(g => g.Label).ToArray());
        Assert.Equal(0.4, live.Gauges[0].Used, 3);
    }

    [Fact]
    public void AntigravityPoolKeepsTheEarliestRenewal()
    {
        const string json = """
        {"userStatus":{"cascadeModelConfigData":{"clientModelConfigs":[
          {"label":"Gemini 3.8 Flash (High)","quotaInfo":{"remainingFraction":0.9,"resetTime":"2026-11-11T14:00:00Z"}},
          {"label":"Gemini 3.8 Flash (Low)","quotaInfo":{"remainingFraction":0.95}},
          {"label":"Gemini 3.8 Flash (Max)","quotaInfo":{"remainingFraction":0.99,"resetTime":"2026-11-11T12:00:00Z"}}]}}}
        """;
        var gauge = Assert.Single(AiUsageParsing.ReadAntigravityLive(json, Now)!.Gauges);
        Assert.Equal(0.1, gauge.Used, 3);
        Assert.Equal(DateTimeOffset.Parse("2026-11-11T12:00:00Z"), gauge.RenewsAt);
    }

    [Fact]
    public void RenewalTextUsesMochiDocksWording()
    {
        Assert.Equal("Renews in 1m", AiUsageParsing.RenewalText(Now.AddSeconds(10), Now));
        Assert.Equal("Renews soon", AiUsageParsing.RenewalText(Now, Now));
    }
}
