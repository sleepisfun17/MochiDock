using System.Text;
using MochiDock.Core;

public class AiUsageParsingTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T10:00:00Z");

    [Fact]
    public void ClaudeReadsUtilizationAndSkipsNullWindows()
    {
        const string json = """{"five_hour":{"utilization":62.0,"resets_at":"2026-11-11T13:00:00Z"},"seven_day":{"utilization":18.5,"resets_at":"2026-11-15T00:00:00Z"},"seven_day_opus":null,"seven_day_sonnet":null,"extra_usage":null}""";
        var windows = AiUsageParsing.ReadClaudeLimits(json);
        Assert.Equal(2, windows.Count);
        Assert.Equal("5 hours", windows[0].Label);
        Assert.Equal(0.62, windows[0].Used, 3);
        Assert.Equal("7 days", windows[1].Label);
        Assert.Equal(0.185, windows[1].Used, 3);
        Assert.Equal("Renews in 3h", AiUsageParsing.RenewalText(windows[0].RenewsAt, Now));
    }

    [Fact]
    public void ClaudeAcceptsOldFieldNameAndRejectsGarbage()
    {
        Assert.Single(AiUsageParsing.ReadClaudeLimits("""{"five_hour":{"used_percent":10}}"""));
        Assert.Empty(AiUsageParsing.ReadClaudeLimits("not json"));
        Assert.Empty(AiUsageParsing.ReadClaudeLimits("[]"));
        Assert.Empty(AiUsageParsing.ReadClaudeLimits("""{"five_hour":{"resets_at":"2026-11-11T13:00:00Z"}}"""));
    }

    [Fact]
    public void ClaudeClampsOutOfRangeUtilization()
    {
        var windows = AiUsageParsing.ReadClaudeLimits("""{"five_hour":{"utilization":140},"seven_day":{"utilization":-5}}""");
        Assert.Equal(1.0, windows[0].Used);
        Assert.Equal(0.0, windows[1].Used);
    }

    static string Jwt(string payload) =>
        "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";

    [Fact]
    public void JwtReadsUserIdAndExpiry()
    {
        var claims = AiUsageParsing.ReadCursorSession(Jwt("""{"sub":"auth0|user_01ABC","exp":1795000000}"""));
        Assert.NotNull(claims);
        Assert.Equal("user_01ABC", claims!.UserId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795000000), claims.ExpiresAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nodots")]
    [InlineData("a.b.c")]
    public void JwtRejectsMalformedTokens(string? token) => Assert.Null(AiUsageParsing.ReadCursorSession(token));

    [Fact]
    public void JwtRejectsUserIdsThatCouldInjectIntoACookie()
    {
        Assert.Null(AiUsageParsing.ReadCursorSession(Jwt("""{"sub":"auth0|bad;id","exp":1795000000}""")));
        Assert.Null(AiUsageParsing.ReadCursorSession(Jwt("""{"sub":"auth0|user_01","exp":0}""")));
    }

    [Fact]
    public void CursorPercentsAreAlreadyPercentUnits()
    {
        const string json = """{"membershipType":"pro","billingCycleEnd":"2026-12-01T00:00:00Z","individualUsage":{"plan":{"totalPercentUsed":36.0,"autoPercentUsed":20.0,"apiPercentUsed":52.0}}}""";
        var reading = AiUsageParsing.ReadCursorSummary(json);
        Assert.NotNull(reading);
        Assert.Equal("Pro", reading!.Plan);
        Assert.Equal(3, reading.Gauges.Count);
        Assert.Equal(0.36, reading.Gauges[0].Used, 3);
        Assert.Equal("Auto, month", reading.Gauges[1].Label);
        Assert.Equal("Renews in 20d", AiUsageParsing.RenewalText(reading.Gauges[0].RenewsAt, Now));
    }

    [Fact]
    public void CursorFallsBackToMeanOfPartsThenCents()
    {
        var mean = AiUsageParsing.ReadCursorSummary("""{"individualUsage":{"plan":{"autoPercentUsed":10,"apiPercentUsed":30}}}""");
        Assert.Equal(0.20, mean!.Gauges[0].Used, 3);
        var cents = AiUsageParsing.ReadCursorSummary("""{"membershipType":"free","individualUsage":{"plan":{"used":500,"limit":2000}}}""");
        Assert.Equal(0.25, cents!.Gauges[0].Used, 3);
    }

    [Fact]
    public void CursorRejectsAnswersWithoutUsage()
    {
        Assert.Null(AiUsageParsing.ReadCursorSummary("{}"));
        Assert.Null(AiUsageParsing.ReadCursorSummary("oops"));
    }

    [Fact]
    public void GeminiGroupsBucketsByFamilyUsingTheTightestOne()
    {
        const string json = """
            {"buckets":[
              {"modelId":"gemini-2.5-pro","remainingFraction":0.8,"resetTime":"2026-11-12T00:00:00Z"},
              {"modelId":"gemini-3-pro-preview","remainingFraction":0.4,"resetTime":"2026-11-12T00:00:00Z"},
              {"modelId":"gemini-2.5-flash","remainingFraction":0.95},
              {"modelId":"gemini-2.5-flash-lite","remainingFraction":1.0},
              {"modelId":"embedding-001","remainingFraction":0.1}
            ]}
            """;
        var windows = AiUsageParsing.ReadGeminiQuota(json);
        Assert.Equal(new[] { "Pro", "Flash", "Flash-Lite" }, windows.Select(w => w.Label));
        Assert.Equal(0.6, windows[0].Used, 3);
        Assert.Equal(0.05, windows[1].Used, 3);
        Assert.Equal(0.0, windows[2].Used, 3);
    }

    [Fact]
    public void GeminiLoadReadsProjectAndTierInEitherShape()
    {
        var a = AiUsageParsing.ReadCodeAssistAccount("""{"cloudaicompanionProject":"proj-1","currentTier":{"name":"Standard"},"paidTier":{"name":"Google AI Pro"}}""");
        Assert.Equal("proj-1", a.ProjectId);
        Assert.Equal("Google AI Pro", a.Tier);
        var b = AiUsageParsing.ReadCodeAssistAccount("""{"cloudaicompanionProject":{"id":"proj-2"},"currentTier":{"name":"Standard"}}""");
        Assert.Equal("proj-2", b.ProjectId);
        Assert.Equal("Standard", b.Tier);
        Assert.Null(AiUsageParsing.ReadCodeAssistAccount("nope").ProjectId);
    }

    [Theory]
    [InlineData("""{"error":{"status":"PERMISSION_DENIED","message":"UNSUPPORTED_CLIENT"}}""", true)]
    [InlineData("""{"reason":"SUBSCRIPTION_REQUIRED"}""", true)]
    [InlineData("""{"error":"quota"}""", false)]
    public void GeminiDetectsRefusedPersonalAccounts(string body, bool expected) => Assert.Equal(expected, AiUsageParsing.IsPersonalGeminiRefusal(body));

    [Fact]
    public void CopilotReadsQuotaSnapshotsAndSkipsUnlimited()
    {
        const string json = """
            {"copilot_plan":"individual_pro","quota_reset_date_utc":"2026-12-01T00:00:00Z","quota_snapshots":{
              "premium_interactions":{"entitlement":300,"quota_remaining":210,"percent_remaining":70.0,"unlimited":false},
              "chat":{"unlimited":true,"percent_remaining":100},
              "completions":{"entitlement":2000,"quota_remaining":500}
            }}
            """;
        var reading = AiUsageParsing.ReadCopilot(json);
        Assert.NotNull(reading);
        Assert.Equal("Individual pro", reading!.Plan);
        Assert.Equal(2, reading.Gauges.Count);
        Assert.Equal("Premium", reading.Gauges[0].Label);
        Assert.Equal(0.30, reading.Gauges[0].Used, 3);
        Assert.Equal(0.75, reading.Gauges[1].Used, 3);
    }

    [Fact]
    public void CopilotWithoutQuotaHasNoWindowsInsteadOfAFakeOne()
    {
        var reading = AiUsageParsing.ReadCopilot("""{"copilot_plan":"business"}""");
        Assert.Empty(reading!.Gauges);
    }

    private sealed record P(string Id, double[] Used);

    private static string[] Order(params P[] ps) =>
        AiUsageParsing.RankByHeadroom(ps, p => p.Id, p => p.Used).Select(p => p.Id).ToArray();

    [Fact]
    public void ProvidersWithMostUsageLeftComeFirst()
    {
        Assert.Equal(new[] { "codex", "claude", "antigravity" },
            Order(new P("claude", new[] { 0.5 }), new P("antigravity", new[] { 0.9 }), new P("codex", new[] { 0.1 })));
    }

    [Fact]
    public void TightestWindowDecidesUsageLeft()
    {
        Assert.Equal(new[] { "codex", "claude" },
            Order(new P("claude", new[] { 0.1, 0.95 }), new P("codex", new[] { 0.4, 0.5 })));
    }

    [Fact]
    public void ProvidersWithoutWindowsSortLastAndTiesUsePriority()
    {
        Assert.Equal(new[] { "claude", "antigravity", "codex", "cursor" },
            Order(new P("cursor", Array.Empty<double>()), new P("codex", new[] { 0.2 }), new P("antigravity", new[] { 0.2 }), new P("claude", new[] { 0.2 })));
    }

    // ---- Claude desktop app usage history ------------------------------------------------------------------------

    private static readonly DateTimeOffset AppNow = DateTimeOffset.FromUnixTimeMilliseconds(1_794_000_000_000);
    private static string Sample(long ms, string u, string org = "o1") => $$"""{"t":{{ms}},"org":"{{org}}","u":{{u}}}""";
    private static string History(params string[] samples) => $$"""{"version":2,"samples":[{{string.Join(",", samples)}}]}""";
    private static readonly TimeSpan AppMaxAge = TimeSpan.FromMinutes(90);

    [Fact]
    public void ClaudeAppHistoryUsesTheLatestSample()
    {
        var json = History(Sample(1_793_999_000_000, """{"fh":10,"sd":5}"""), Sample(1_793_999_900_000, """{"fh":30,"sd":17}"""), Sample(1_793_999_500_000, """{"fh":20,"sd":9}"""));
        var usage = AiUsageParsing.ReadClaudeAppHistory(json, AppNow, AppMaxAge);
        Assert.NotNull(usage);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_793_999_900_000), usage!.TakenAt);
        Assert.Equal(new[] { "5 hours", "7 days" }, usage.Gauges().Select(w => w.Label).ToArray());
        Assert.Equal(0.30, usage.Gauges()[0].Used, 3);
        Assert.Equal(0.17, usage.Gauges()[1].Used, 3);
        Assert.All(usage.Gauges(), w => Assert.Null(w.RenewsAt));
    }

    [Fact]
    public void ClaudeAppHistoryKeepsASingleWindowAndClampsValues()
    {
        var only = AiUsageParsing.ReadClaudeAppHistory(History(Sample(1_793_999_900_000, """{"sd":120}""")), AppNow, AppMaxAge);
        Assert.Single(only!.Gauges());
        Assert.Equal("7 days", only.Gauges()[0].Label);
        Assert.Equal(1.0, only.Gauges()[0].Used);
    }

    [Fact]
    public void ClaudeAppHistorySkipsEmptySamplesAndFallsBackToAnOlderOne()
    {
        var json = History(Sample(1_793_999_000_000, """{"fh":12,"sd":4}"""), Sample(1_793_999_900_000, "{}"));
        var usage = AiUsageParsing.ReadClaudeAppHistory(json, AppNow, AppMaxAge);
        Assert.Equal(0.12, usage!.Gauges()[0].Used, 3);
    }

    [Fact]
    public void ClaudeAppHistoryRejectsStaleBrokenAndUnknownShapes()
    {
        Assert.Null(AiUsageParsing.ReadClaudeAppHistory(History(Sample(1_793_990_000_000, """{"fh":1,"sd":1}""")), AppNow, AppMaxAge)); // older than 90 min
        Assert.Null(AiUsageParsing.ReadClaudeAppHistory("not json", AppNow, AppMaxAge));
        Assert.Null(AiUsageParsing.ReadClaudeAppHistory("""{"version":3,"samples":[]}""", AppNow, AppMaxAge));
        Assert.Null(AiUsageParsing.ReadClaudeAppHistory("""{"version":2,"samples":"x"}""", AppNow, AppMaxAge));
        Assert.Null(AiUsageParsing.ReadClaudeAppHistory(History(Sample(1_793_999_900_000, "{}")), AppNow, AppMaxAge));
    }

    [Fact]
    public void ClaudeAppHistoryKeepsOldSamplesWhenAskedToForTheLastKnownReading()
    {
        var old = History(Sample(1_793_990_000_000, """{"fh":40,"sd":22}"""));
        var usage = AiUsageParsing.ReadClaudeAppHistory(old, AppNow, TimeSpan.FromDays(7));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_793_990_000_000), usage!.TakenAt);
        Assert.Equal(0.22, usage.Gauges()[1].Used, 3);
    }

    // ---- Antigravity userStatus ------------------------------------------------------------------------------------

    private static byte[] Varint(ulong v)
    {
        var bytes = new List<byte>();
        while (v >= 0x80) { bytes.Add((byte)(v | 0x80)); v >>= 7; }
        bytes.Add((byte)v);
        return bytes.ToArray();
    }

    private static byte[] Len(int field, byte[] payload) => Varint((ulong)(field << 3 | 2)).Concat(Varint((ulong)payload.Length)).Concat(payload).ToArray();
    private static byte[] Num(int field, ulong value) => Varint((ulong)(field << 3)).Concat(Varint(value)).ToArray();
    private static byte[] Float(int field, float value) => Varint((ulong)(field << 3 | 5)).Concat(BitConverter.GetBytes(value)).ToArray();

    private static byte[] AgModel(string name, float remaining, long reset) =>
        Len(1, Encoding.UTF8.GetBytes(name))
            .Concat(Len(15, Float(1, remaining).Concat(Len(2, Num(1, (ulong)reset))).ToArray()))
            .ToArray();

    private static string AgUserStatus(params byte[][] models)
    {
        var status = Len(3, Encoding.UTF8.GetBytes("user")).Concat(Len(33, models.SelectMany(m => Len(1, m)).ToArray())).ToArray();
        var inner = Convert.ToBase64String(status);
        var envelope = Len(1, Encoding.UTF8.GetBytes("userStatusSentinelKey")).Concat(Len(2, Len(1, Encoding.ASCII.GetBytes(inner)))).ToArray();
        return Convert.ToBase64String(Len(1, envelope));
    }

    static readonly long AgNow = Now.ToUnixTimeSeconds();

    [Fact]
    public void AntigravityGroupsModelsIntoPoolsAndReportsTheTightestOne()
    {
        var raw = AgUserStatus(
            AgModel("Gemini 3.8 Flash (High)", 0.8f, AgNow + 7200),
            AgModel("Gemini 3.8 Flash (Low)", 0.6f, AgNow + 3600),
            AgModel("Claude Sonnet 4.6 (Thinking)", 1.0f, AgNow + 90000),
            AgModel("GPT-OSS 120B (Medium)", 0.9f, AgNow + 90000));
        var windows = AiUsageParsing.ReadAntigravityStoredStatus(raw, Now);
        Assert.Equal(new[] { "Gemini Flash", "Claude + GPT" }, windows.Select(w => w.Label).ToArray());
        Assert.Equal(0.4, windows[0].Used, 3);
        Assert.Equal("Renews in 1h", AiUsageParsing.RenewalText(windows[0].RenewsAt, Now));
        Assert.Equal(0.1, windows[1].Used, 3);
    }

    [Fact]
    public void AntigravityTreatsAPassedResetAsRenewedAndAcceptsJsonWrapping()
    {
        var raw = AgUserStatus(AgModel("Gemini 3.1 Pro (High)", 0.1f, AgNow - 60));
        var windows = AiUsageParsing.ReadAntigravityStoredStatus($$"""{"data":"{{raw}}"}""", Now);
        Assert.Single(windows);
        Assert.Equal("Gemini Pro", windows[0].Label);
        Assert.Equal(0.0, windows[0].Used, 3);
        Assert.Null(windows[0].RenewsAt);
    }

    [Fact]
    public void AntigravityLiveStatusReadsQuotaInfoPerModelAndSkipsDisabledOnes()
    {
        const string json = """
        {"userStatus":{"userTier":{"name":"Google AI Pro"},"cascadeModelConfigData":{"clientModelConfigs":[
          {"label":"Gemini 4 Argon (High)","quotaInfo":{"remainingFraction":0.2,"resetTime":"2026-11-13T10:00:00Z"}},
          {"label":"Gemini 4 Argon (Low)","quotaInfo":{"remainingFraction":0.25,"resetTime":"2026-11-13T10:00:00Z"}},
          {"label":"Gemini 3.8 Flash (High)","quotaInfo":{"remainingFraction":0.7,"resetTime":"2026-11-11T09:00:00Z"}},
          {"label":"Claude Opus 5.5 (High)","quotaInfo":{"remainingFraction":0.5,"resetTime":"2026-11-11T15:00:00Z"}},
          {"label":"Gemini 4 Carbon","disabled":true,"quotaInfo":{}}]}}}
        """;
        var live = AiUsageParsing.ReadAntigravityLive(json, Now);
        Assert.NotNull(live);
        Assert.Equal("Google AI Pro", live!.Plan);
        Assert.Equal(new[] { "Gemini 4 Argon", "Gemini Flash", "Claude + GPT" }, live.Gauges.Select(w => w.Label).ToArray());
        Assert.Equal(0.8, live.Gauges[0].Used, 3);
        Assert.Equal("Renews in 2d", AiUsageParsing.RenewalText(live.Gauges[0].RenewsAt, Now));
        Assert.Equal(0.0, live.Gauges[1].Used, 3); // reset already passed -> renewed
        Assert.Equal(0.5, live.Gauges[2].Used, 3);
        Assert.Null(AiUsageParsing.ReadAntigravityLive("""{"userStatus":{}}""", Now));
        Assert.Null(AiUsageParsing.ReadAntigravityLive("nope", Now));
    }

    [Fact]
    public void AntigravityQuotaSummaryReadsWeeklyAndFiveHourBucketsPerGroup()
    {
        const string json = """
        {"response":{"groups":[
          {"displayName":"Gemini Models","buckets":[{"window":"weekly","remainingFraction":0.376,"resetTime":"2026-11-13T10:00:00Z"},{"window":"5h","remainingFraction":1,"resetTime":"2026-11-11T13:00:00Z"}]},
          {"displayName":"Claude and GPT models","buckets":[{"window":"weekly","remainingFraction":0.5,"resetTime":"2026-11-11T09:00:00Z"}]},
          {"displayName":"Gemini 4 Argon","buckets":[{"window":"weekly","remainingFraction":0.2,"resetTime":"2026-11-14T10:00:00Z"}]}]}}
        """;
        var w = AiUsageParsing.ReadAntigravityQuotaSummary(json, Now);
        Assert.Equal(new[] { "Gemini, 7 days", "Gemini, 5 hours", "Claude + GPT, 7 days", "Argon, 7 days" }, w.Select(x => x.Label).ToArray());
        Assert.Equal(0.624, w[0].Used, 3);
        Assert.Equal("Renews in 3h", AiUsageParsing.RenewalText(w[1].RenewsAt, Now));
        Assert.Equal(0.0, w[2].Used, 3); // reset passed -> renewed
        Assert.Equal(0.8, w[3].Used, 3);
        Assert.Equal(new[] { "Gemini, 7 days", "Gemini, 5 hours", "Argon, 7 days" }, AiUsageParsing.ReadAntigravityQuotaSummary(json, Now, includePartnerPool: false).Select(x => x.Label).ToArray());
        Assert.Empty(AiUsageParsing.ReadAntigravityQuotaSummary("nope", Now));
        Assert.Empty(AiUsageParsing.ReadAntigravityQuotaSummary("""{"response":{}}""", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    [InlineData("""{"data":5}""")]
    public void AntigravityRejectsGarbageWithoutThrowing(string raw) => Assert.Empty(AiUsageParsing.ReadAntigravityStoredStatus(raw, Now));

    [Theory]
    [InlineData(-5, "Renews soon")]
    [InlineData(20, "Renews in 20m")]
    [InlineData(90, "Renews in 2h")]
    [InlineData(60 * 49, "Renews in 3d")]
    public void ResetTextRoundsUp(int minutes, string expected) => Assert.Equal(expected, AiUsageParsing.RenewalText(Now.AddMinutes(minutes), Now));

    [Fact]
    public void ResetTextIsEmptyWhenUnknown() => Assert.Equal("", AiUsageParsing.RenewalText(null, Now));
}
