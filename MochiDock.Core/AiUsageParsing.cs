using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MochiDock.Core;

/// <summary>One usage limit of a plan: how much of it is spent (0..1) and when it fills up again, if the provider says.</summary>
public sealed record QuotaGauge(string Label, double Used, DateTimeOffset? RenewsAt);

/// <summary>A plan name (may be empty when the provider does not report one) and its limits.</summary>
public sealed record PlanReading(string Plan, IReadOnlyList<QuotaGauge> Gauges);

/// <summary>What Cursor's sign-in token says about itself: the account id used in the session cookie and when it lapses.</summary>
public sealed record CursorSession(string UserId, DateTimeOffset ExpiresAt);

/// <summary>Gemini Code Assist account facts from loadCodeAssist.</summary>
public sealed record CodeAssistAccount(string? ProjectId, string? Tier);

/// <summary>
/// The newest usage sample the Claude desktop app wrote. Percentages are spent shares (0..1); either may be missing.
/// The file carries no renewal times.
/// </summary>
public sealed record ClaudeAppSample(DateTimeOffset TakenAt, double? FiveHourUsed, double? SevenDayUsed)
{
    /// <summary>The sample as gauges. A short-window figure can be left out once it no longer describes the current window.</summary>
    public IReadOnlyList<QuotaGauge> Gauges(bool includeFiveHour = true)
    {
        var list = new List<QuotaGauge>(2);
        if (includeFiveHour && FiveHourUsed is { } shortSpent) list.Add(new QuotaGauge(UsageLabels.FiveHours, shortSpent, null));
        if (SevenDayUsed is { } weekSpent) list.Add(new QuotaGauge(UsageLabels.SevenDays, weekSpent, null));
        return list;
    }
}

/// <summary>What the running Antigravity IDE reports: the tier name and the quota pools.</summary>
public sealed record AntigravityLiveReading(string Plan, IReadOnlyList<QuotaGauge> Gauges);

/// <summary>The words MochiDock shows next to each usage bar.</summary>
public static class UsageLabels
{
    public const string FiveHours = "5 hours";
    public const string SevenDays = "7 days";
    public const string OpusSevenDays = "Opus, 7 days";
    public const string SonnetSevenDays = "Sonnet, 7 days";
    public const string CursorMonth = "Month";
    public const string CursorAutoMonth = "Auto, month";
    public const string CursorApiMonth = "API, month";
    public const string GeminiPro = "Pro";
    public const string GeminiFlash = "Flash";
    public const string GeminiFlashLite = "Flash-Lite";
    public const string CopilotPremium = "Premium";
    public const string CopilotChat = "Chat";
    public const string CopilotCompletions = "Completions";
    public const string AntigravityPartners = "Claude + GPT";

    /// <summary>A limit's length in words: "5 hours", "1 day", "7 days".</summary>
    public static string ForSpan(TimeSpan span)
    {
        if (span.TotalHours >= 23.5)
        {
            int days = Math.Max(1, (int)Math.Round(span.TotalDays));
            return days == 1 ? "1 day" : $"{days} days";
        }
        int hours = Math.Max(1, (int)Math.Round(span.TotalHours));
        return hours == 1 ? "1 hour" : $"{hours} hours";
    }
}

/// <summary>
/// Turns the answers and local files of AI coding tools into plan readings. Pure: no network, files or WPF, so every
/// reader can be tested with captured payloads. Anything that is not the expected shape gives null or an empty list; nothing throws
/// and nothing is guessed.
/// </summary>
public static class AiUsageParsing
{
    // ---- shared presentation ---------------------------------------------------------------------------------------

    /// <summary>"Renews in 3h" style text, rounded up; "" when the time is unknown.</summary>
    public static string RenewalText(DateTimeOffset? renewsAt, DateTimeOffset now)
    {
        if (renewsAt is not { } at) return "";
        var wait = at - now;
        if (wait <= TimeSpan.Zero) return "Renews soon";
        string amount = wait.TotalHours >= 24 ? $"{Math.Ceiling(wait.TotalDays)}d"
            : wait.TotalHours >= 1 ? $"{Math.Ceiling(wait.TotalHours)}h"
            : $"{Math.Max(1, Math.Ceiling(wait.TotalMinutes))}m";
        return "Renews in " + amount;
    }

    /// <summary>Share still available (0..100, one decimal) in the most spent limit; null when there are no limits.</summary>
    public static double? Headroom(IReadOnlyList<double> spentShares)
    {
        if (spentShares.Count == 0) return null;
        double worst = spentShares.Select(Unit).Max();
        return Math.Round((1.0 - worst) * 100.0, 1);
    }

    private static readonly string[] TieBreak = { "claude", "antigravity", "codex" };

    /// <summary>
    /// Sorts providers so the one with the most headroom comes first. Providers without limits go to the end.
    /// Equal headroom: Claude Code, then Antigravity, then Codex, then the order given.
    /// </summary>
    public static List<T> RankByHeadroom<T>(IEnumerable<T> providers, Func<T, string> idOf, Func<T, IReadOnlyList<double>> spentOf)
    {
        int TieRank(string id)
        {
            for (int i = 0; i < TieBreak.Length; i++)
                if (string.Equals(TieBreak[i], id, StringComparison.OrdinalIgnoreCase)) return i;
            return TieBreak.Length;
        }

        var keyed = providers.Select((p, position) => new { p, position, room = Headroom(spentOf(p)), tie = TieRank(idOf(p)) }).ToList();
        keyed.Sort((a, b) =>
        {
            if (a.room.HasValue != b.room.HasValue) return a.room.HasValue ? -1 : 1;
            int byRoom = (b.room ?? 0).CompareTo(a.room ?? 0);
            if (byRoom != 0) return byRoom;
            int byTie = a.tie.CompareTo(b.tie);
            return byTie != 0 ? byTie : a.position.CompareTo(b.position);
        });
        return keyed.Select(k => k.p).ToList();
    }

    // ---- Claude Code: api.anthropic.com/api/oauth/usage -------------------------------------------------------------

    private static readonly (string Field, string Label)[] ClaudeLimitFields =
    {
        ("five_hour", UsageLabels.FiveHours),
        ("seven_day", UsageLabels.SevenDays),
        ("seven_day_opus", UsageLabels.OpusSevenDays),
        ("seven_day_sonnet", UsageLabels.SonnetSevenDays),
    };

    /// <summary>Reads the Claude usage answer. Limits sent as null (not part of the plan) are left out.</summary>
    public static List<QuotaGauge> ReadClaudeLimits(string json)
    {
        var gauges = new List<QuotaGauge>();
        using var doc = JsonPick.Open(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return gauges;
        foreach (var (field, label) in ClaudeLimitFields)
        {
            var limit = JsonPick.Child(doc.RootElement, field);
            if (limit is not { } l) continue;
            var percent = JsonPick.Number(l, "utilization") ?? JsonPick.Number(l, "used_percent");
            if (percent is not { } p) continue;
            gauges.Add(new QuotaGauge(label, Unit(p / 100.0), JsonPick.Moment(l, "resets_at") ?? JsonPick.Moment(l, "reset_at")));
        }
        return gauges;
    }

    // ---- Claude desktop app: plan-usage-history.json ------------------------------------------------------------------
    // {"version":2,"samples":[{"t":<unix ms>,"org":"...","u":{"fh":<5-hour %>,"sd":<7-day %>}}]}

    /// <summary>The newest sample that has a figure, if it is no older than <paramref name="maxAge"/>.</summary>
    public static ClaudeAppSample? ReadClaudeAppHistory(string json, DateTimeOffset now, TimeSpan maxAge)
    {
        using var doc = JsonPick.Open(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = doc.RootElement;
        if (JsonPick.Number(root, "version") is not (1 or 2)) return null;
        if (!root.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array) return null;

        double newest = 0;
        double? shortPct = null, weekPct = null;
        foreach (var sample in samples.EnumerateArray())
        {
            if (JsonPick.Number(sample, "t") is not { } stamp || stamp <= newest) continue;
            if (JsonPick.Child(sample, "u") is not { } figures) continue;
            var fh = JsonPick.Number(figures, "fh");
            var sd = JsonPick.Number(figures, "sd");
            if (fh is null && sd is null) continue;
            (newest, shortPct, weekPct) = (stamp, fh, sd);
        }
        if (newest <= 0) return null;

        DateTimeOffset takenAt;
        try { takenAt = DateTimeOffset.FromUnixTimeMilliseconds((long)newest); }
        catch (ArgumentOutOfRangeException) { return null; }
        if (now - takenAt > maxAge) return null;

        return new ClaudeAppSample(takenAt, shortPct is { } a ? Unit(a / 100.0) : null, weekPct is { } b ? Unit(b / 100.0) : null);
    }

    // ---- Codex: chatgpt.com/backend-api/wham/usage and local rollout logs --------------------------------------------

    private static readonly TimeSpan FiveHourSpan = TimeSpan.FromHours(5);
    private static readonly TimeSpan SevenDaySpan = TimeSpan.FromDays(7);

    /// <summary>Reads the ChatGPT usage answer (rate_limit.primary_window / secondary_window). Null when it has no limits.</summary>
    public static PlanReading? ReadCodexUsage(string json)
    {
        using var doc = JsonPick.Open(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = doc.RootElement;
        var gauges = new List<QuotaGauge>();
        if (JsonPick.Child(root, "rate_limit") is { } limits)
        {
            AddCodexGauge(gauges, JsonPick.Child(limits, "primary_window"), "limit_window_seconds", 1, FiveHourSpan);
            AddCodexGauge(gauges, JsonPick.Child(limits, "secondary_window"), "limit_window_seconds", 1, SevenDaySpan);
        }
        return gauges.Count == 0 ? null : new PlanReading(CodexPlanName(JsonPick.Text(root, "plan_type")), gauges);
    }

    /// <summary>Reads one line of a Codex rollout log; only token_count events carry rate limits.</summary>
    public static PlanReading? ReadCodexRolloutLine(string line)
    {
        using var doc = JsonPick.Open(line);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = doc.RootElement;
        if (JsonPick.Text(root, "type") != "event_msg") return null;
        if (JsonPick.Child(root, "payload") is not { } payload || JsonPick.Text(payload, "type") != "token_count") return null;
        if (JsonPick.Child(payload, "rate_limits") is not { } limits) return null;

        var gauges = new List<QuotaGauge>();
        AddCodexGauge(gauges, JsonPick.Child(limits, "primary"), "window_minutes", 60, FiveHourSpan);
        AddCodexGauge(gauges, JsonPick.Child(limits, "secondary"), "window_minutes", 60, SevenDaySpan);
        return gauges.Count == 0 ? null : new PlanReading(CodexPlanName(JsonPick.Text(limits, "plan_type")), gauges);
    }

    // A limit without a used share is skipped rather than shown as empty. A missing length falls back to the usual one for its slot.
    private static void AddCodexGauge(List<QuotaGauge> into, JsonElement? limit, string lengthField, double secondsPerUnit, TimeSpan usualSpan)
    {
        if (limit is not { } l || JsonPick.Number(l, "used_percent") is not { } percent) return;
        var span = JsonPick.Number(l, lengthField) is { } units && units > 0 ? TimeSpan.FromSeconds(units * secondsPerUnit) : usualSpan;
        into.Add(new QuotaGauge(UsageLabels.ForSpan(span), Unit(percent / 100.0), JsonPick.Moment(l, "resets_at") ?? JsonPick.Moment(l, "reset_at")));
    }

    /// <summary>Display name for a ChatGPT plan id; empty when the plan is not reported.</summary>
    public static string CodexPlanName(string? planId)
    {
        if (string.IsNullOrWhiteSpace(planId)) return "";
        string id = planId.Trim().ToLowerInvariant();
        return id switch
        {
            "free" or "free_workspace" => "Free",
            "prolite" or "pro_lite" => "Pro Lite",
            "promax" or "pro_max" => "Pro Max",
            _ => SentenceCase(id)
        };
    }

    // ---- Cursor ---------------------------------------------------------------------------------------------------

    private static bool IsTokenSafe(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.';

    /// <summary>Reads the JWT payload of a Cursor token. Null when it is malformed or the account id could break the cookie.</summary>
    public static CursorSession? ReadCursorSession(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var segments = token.Trim().Split('.');
        if (segments.Length < 2 || DecodeBase64Url(segments[1]) is not { } payloadBytes) return null;
        using var doc = JsonPick.Open(Encoding.UTF8.GetString(payloadBytes));
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;

        string? subject = JsonPick.Text(doc.RootElement, "sub");
        if (subject is null) return null;
        int bar = subject.LastIndexOf('|');
        string userId = bar >= 0 ? subject[(bar + 1)..] : subject;
        if (userId.Length == 0 || !userId.All(IsTokenSafe)) return null;
        if (JsonPick.Number(doc.RootElement, "exp") is not { } exp || exp <= 0) return null;
        try { return new CursorSession(userId, DateTimeOffset.FromUnixTimeSeconds((long)exp)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>True when every character of the token is safe to put in a cookie.</summary>
    public static bool IsCookieSafeToken(string token) => token.Length > 0 && token.All(IsTokenSafe);

    /// <summary>Decodes base64 or base64url, with or without padding; null when it is not base64.</summary>
    public static byte[]? DecodeBase64Url(string value)
    {
        var text = new StringBuilder(value.Trim()).Replace('-', '+').Replace('_', '/');
        while (text.Length % 4 != 0) text.Append('=');
        try { return Convert.FromBase64String(text.ToString()); }
        catch (FormatException) { return null; }
    }

    /// <summary>Reads Cursor's usage-summary answer. The percent fields are percent units (0.36 means 0.36 %).</summary>
    public static PlanReading? ReadCursorSummary(string json)
    {
        using var doc = JsonPick.Open(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = doc.RootElement;
        bool answered = new[] { "individualUsage", "membershipType", "billingCycleEnd" }
            .Any(k => root.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null);
        if (!answered) return null;

        var renews = JsonPick.Moment(root, "billingCycleEnd");
        var gauges = new List<QuotaGauge>();
        if (JsonPick.Child(root, "individualUsage") is { } individual && JsonPick.Child(individual, "plan") is { } plan)
        {
            double? auto = PercentShare(plan, "autoPercentUsed");
            double? api = PercentShare(plan, "apiPercentUsed");
            double? total = PercentShare(plan, "totalPercentUsed")
                ?? (auto.HasValue && api.HasValue ? (auto + api) / 2.0 : auto ?? api)
                ?? Ratio(JsonPick.Number(plan, "used"), JsonPick.Number(plan, "limit"));
            if (total is { } t) gauges.Add(new QuotaGauge(UsageLabels.CursorMonth, Unit(t), renews));
            if (auto is { } a) gauges.Add(new QuotaGauge(UsageLabels.CursorAutoMonth, Unit(a), renews));
            if (api is { } p) gauges.Add(new QuotaGauge(UsageLabels.CursorApiMonth, Unit(p), renews));
        }
        return new PlanReading(SentenceCase(JsonPick.Text(root, "membershipType")), gauges);
    }

    private static double? PercentShare(JsonElement e, string field) => JsonPick.Number(e, field) is { } v ? v / 100.0 : null;

    // ---- Gemini CLI (Code Assist) -----------------------------------------------------------------------------------

    /// <summary>Reads loadCodeAssist: the companion project (a string or {id}) and the tier, paid tier first.</summary>
    public static CodeAssistAccount ReadCodeAssistAccount(string json)
    {
        using var doc = JsonPick.Open(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return new CodeAssistAccount(null, null);
        var root = doc.RootElement;
        string? project = JsonPick.Text(root, "cloudaicompanionProject")
            ?? (JsonPick.Child(root, "cloudaicompanionProject") is { } p ? JsonPick.Text(p, "id") : null);
        string? tier = null;
        foreach (var field in new[] { "paidTier", "currentTier" })
        {
            if (JsonPick.Child(root, field) is { } t && JsonPick.Text(t, "name") is { } name) { tier = name; break; }
        }
        return new CodeAssistAccount(project, tier);
    }

    private static readonly (string Marker, string Label)[] GeminiFamilies =
    {
        ("flash-lite", UsageLabels.GeminiFlashLite), ("flash", UsageLabels.GeminiFlash), ("pro", UsageLabels.GeminiPro),
    };

    /// <summary>Reads retrieveUserQuota buckets, one gauge per model family (its most spent bucket), Pro, Flash, Flash-Lite.</summary>
    public static List<QuotaGauge> ReadGeminiQuota(string json)
    {
        var worst = new Dictionary<string, QuotaGauge>();
        using var doc = JsonPick.Open(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return new();
        if (!doc.RootElement.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array) return new();

        foreach (var bucket in buckets.EnumerateArray())
        {
            if (JsonPick.Number(bucket, "remainingFraction") is not { } left) continue;
            string model = (JsonPick.Text(bucket, "modelId") ?? "").ToLowerInvariant();
            string? family = GeminiFamilies.Where(f => model.Contains(f.Marker)).Select(f => f.Label).FirstOrDefault();
            if (family is null) continue;
            var gauge = new QuotaGauge(family, Unit(1.0 - left), JsonPick.Moment(bucket, "resetTime"));
            if (!worst.TryGetValue(family, out var seen) || gauge.Used > seen.Used) worst[family] = gauge;
        }
        return new[] { UsageLabels.GeminiPro, UsageLabels.GeminiFlash, UsageLabels.GeminiFlashLite }
            .Where(worst.ContainsKey).Select(f => worst[f]).ToList();
    }

    /// <summary>True when Code Assist turns the account away (personal accounts are no longer served).</summary>
    public static bool IsPersonalGeminiRefusal(string body) =>
        body.Contains("UNSUPPORTED_CLIENT", StringComparison.OrdinalIgnoreCase) || body.Contains("SUBSCRIPTION_REQUIRED", StringComparison.OrdinalIgnoreCase);

    // ---- GitHub Copilot: api.github.com/copilot_internal/user ---------------------------------------------------------

    private static readonly (string Field, string Label)[] CopilotQuotas =
    {
        ("premium_interactions", UsageLabels.CopilotPremium), ("chat", UsageLabels.CopilotChat), ("completions", UsageLabels.CopilotCompletions),
    };

    /// <summary>Reads the Copilot user answer. Unlimited quotas are not drawn; a plan without quotas has no gauges.</summary>
    public static PlanReading? ReadCopilot(string json)
    {
        using var doc = JsonPick.Open(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = doc.RootElement;
        var renews = JsonPick.Moment(root, "quota_reset_date_utc") ?? JsonPick.Moment(root, "quota_reset_date");
        var gauges = new List<QuotaGauge>();
        if (JsonPick.Child(root, "quota_snapshots") is { } snapshots)
        {
            foreach (var (field, label) in CopilotQuotas)
            {
                if (JsonPick.Child(snapshots, field) is not { } quota) continue;
                if (quota.TryGetProperty("unlimited", out var unlimited) && unlimited.ValueKind == JsonValueKind.True) continue;
                double? leftShare = JsonPick.Number(quota, "percent_remaining") is { } pct ? pct / 100.0
                    : Ratio(JsonPick.Number(quota, "quota_remaining"), JsonPick.Number(quota, "entitlement"));
                if (leftShare is { } left) gauges.Add(new QuotaGauge(label, Unit(1.0 - left), renews));
            }
        }
        return new PlanReading(SentenceCase(JsonPick.Text(root, "copilot_plan")), gauges);
    }

    // ---- Antigravity IDE --------------------------------------------------------------------------------------------
    // Models share quota pools. Each pool reports its most spent member and its earliest renewal; a renewal time that has
    // already passed means the pool is full again. The Claude/GPT-OSS partner pool can be left out (the card does).

    private readonly record struct ModelQuota(string Model, double Left, DateTimeOffset? RenewsAt);

    /// <summary>
    /// Reads the IDE's stored state.vscdb value "antigravityUnifiedStateSync.userStatus": base64 (optionally inside {"data": ...})
    /// of a protobuf envelope 1 { 2 { 1: base64 UserStatus } }; UserStatus field 33 lists models as
    /// 1 { 1: name, 15 { 1: float left 0..1, 2 { 1: renewal unix seconds } } }.
    /// </summary>
    public static List<QuotaGauge> ReadAntigravityStoredStatus(string? stored, DateTimeOffset now, bool includePartnerPool = true)
    {
        if (string.IsNullOrWhiteSpace(stored)) return new();
        try
        {
            string text = stored.Trim();
            if (text.StartsWith('{'))
            {
                using var wrapper = JsonPick.Open(text);
                if (wrapper is null || JsonPick.Text(wrapper.RootElement, "data") is not { } data) return new();
                text = data;
            }

            var envelope = Proto.Parse(Convert.FromBase64String(text)).Message(1)?.Message(2)?.Bytes(1);
            if (envelope is null) return new();
            var status = Proto.Parse(Convert.FromBase64String(Encoding.ASCII.GetString(envelope)));

            var models = new List<ModelQuota>();
            foreach (var list in status.Messages(33))
            {
                foreach (var model in list.Messages(1))
                {
                    var name = model.Bytes(1);
                    var quota = model.Message(15);
                    if (name is null || quota?.Fixed32(1) is not { } bits) continue;
                    double left = BitConverter.UInt32BitsToSingle(bits);
                    if (!double.IsFinite(left)) continue;
                    var renewal = quota.Message(2)?.Varint(1) is { } secs && secs > 0 ? JsonPick.FromEpoch((long)Math.Min(secs, long.MaxValue)) : null;
                    models.Add(new ModelQuota(Encoding.UTF8.GetString(name), left, renewal));
                }
            }
            return PoolModels(models, now, includePartnerPool);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or JsonException or OverflowException)
        {
            return new();
        }
    }

    /// <summary>
    /// Reads the language server's GetUserStatus answer: userStatus.userTier.name and
    /// userStatus.cascadeModelConfigData.clientModelConfigs[] { label, quotaInfo { remainingFraction, resetTime } }.
    /// Models with an empty quotaInfo are switched off and skipped. Null when no model has a quota.
    /// </summary>
    public static AntigravityLiveReading? ReadAntigravityLive(string json, DateTimeOffset now, bool includePartnerPool = true)
    {
        using var doc = JsonPick.Open(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (JsonPick.Child(doc.RootElement, "userStatus") is not { } status) return null;

        string tier = JsonPick.Child(status, "userTier") is { } t ? JsonPick.Text(t, "name") ?? "" : "";
        var models = new List<ModelQuota>();
        if (JsonPick.Child(status, "cascadeModelConfigData") is { } data
            && data.TryGetProperty("clientModelConfigs", out var configs) && configs.ValueKind == JsonValueKind.Array)
        {
            foreach (var config in configs.EnumerateArray())
            {
                if (JsonPick.Text(config, "label") is not { } label) continue;
                if (JsonPick.Child(config, "quotaInfo") is not { } quota || JsonPick.Number(quota, "remainingFraction") is not { } left) continue;
                models.Add(new ModelQuota(label, left, JsonPick.Moment(quota, "resetTime")));
            }
        }
        return models.Count == 0 ? null : new AntigravityLiveReading(tier, PoolModels(models, now, includePartnerPool));
    }

    /// <summary>
    /// Reads RetrieveUserQuotaSummary (the IDE's usage panel): (response.)groups[] { displayName, buckets[] { window, remainingFraction, resetTime } }.
    /// A group's models share a 7-day and a 5-hour limit.
    /// </summary>
    public static List<QuotaGauge> ReadAntigravityQuotaSummary(string json, DateTimeOffset now, bool includePartnerPool = true)
    {
        var gauges = new List<QuotaGauge>();
        using var doc = JsonPick.Open(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return gauges;
        var root = JsonPick.Child(doc.RootElement, "response") ?? doc.RootElement;
        if (!root.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array) return gauges;

        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object || !group.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array) continue;
            var (groupName, isPartner) = SummaryGroupName(JsonPick.Text(group, "displayName") ?? "");
            if (isPartner && !includePartnerPool) continue;
            foreach (var bucket in buckets.EnumerateArray())
            {
                if (JsonPick.Number(bucket, "remainingFraction") is not { } left) continue;
                var (fill, renews) = Renewed(left, JsonPick.Moment(bucket, "resetTime"), now);
                string span = (JsonPick.Text(bucket, "window") ?? "").ToLowerInvariant() switch
                {
                    "weekly" => UsageLabels.SevenDays,
                    "5h" => UsageLabels.FiveHours,
                    var other => other
                };
                gauges.Add(new QuotaGauge(span.Length == 0 ? groupName : $"{groupName}, {span}", Unit(1.0 - fill), renews));
            }
        }
        return gauges;
    }

    private static (string Name, bool IsPartner) SummaryGroupName(string displayName)
    {
        if (displayName.StartsWith("Claude", StringComparison.OrdinalIgnoreCase)) return (UsageLabels.AntigravityPartners, true);
        if (displayName.Contains("Argon", StringComparison.OrdinalIgnoreCase)) return ("Argon", false);
        if (displayName.StartsWith("Gemini", StringComparison.OrdinalIgnoreCase)) return ("Gemini", false);
        return (displayName, false);
    }

    private static (string Name, bool IsPartner) ModelPool(string model)
    {
        string name = model.Trim();
        if (name.StartsWith("Claude", StringComparison.OrdinalIgnoreCase) || name.StartsWith("GPT-OSS", StringComparison.OrdinalIgnoreCase))
            return (UsageLabels.AntigravityPartners, true);
        if (name.StartsWith("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Contains("Flash", StringComparison.OrdinalIgnoreCase)) return ("Gemini Flash", false);
            if (name.Contains("Pro", StringComparison.OrdinalIgnoreCase)) return ("Gemini Pro", false);
        }
        if (name.Length == 0) return ("Other", false);
        int variant = name.IndexOf(" (", StringComparison.Ordinal); // "Gemini 4 Argon (High)" belongs to "Gemini 4 Argon"
        return (variant > 0 ? name[..variant] : name, false);
    }

    // A renewal time already behind us means the limit has refilled and its next renewal is not known yet.
    private static (double Left, DateTimeOffset? RenewsAt) Renewed(double left, DateTimeOffset? renewsAt, DateTimeOffset now) =>
        renewsAt is { } at && at <= now ? (1.0, null) : (double.IsFinite(left) ? Unit(left) : 1.0, renewsAt);

    private static List<QuotaGauge> PoolModels(IEnumerable<ModelQuota> models, DateTimeOffset now, bool includePartnerPool)
    {
        var pools = new List<(string Name, double Left, DateTimeOffset? RenewsAt)>();
        foreach (var model in models)
        {
            var (pool, isPartner) = ModelPool(model.Model);
            if (isPartner && !includePartnerPool) continue;
            var (left, renews) = Renewed(model.Left, model.RenewsAt, now);
            int at = pools.FindIndex(p => p.Name == pool);
            if (at < 0) { pools.Add((pool, left, renews)); continue; }
            var known = pools[at];
            var earliest = known.RenewsAt is null ? renews : renews is null ? known.RenewsAt : (renews < known.RenewsAt ? renews : known.RenewsAt);
            pools[at] = (pool, Math.Min(known.Left, left), earliest);
        }
        return pools.Select(p => new QuotaGauge(p.Name, Unit(1.0 - p.Left), p.RenewsAt)).ToList();
    }

    // ---- small helpers ----------------------------------------------------------------------------------------------

    private static double? Ratio(double? part, double? whole) => part is { } p && whole is { } w && w > 0 ? p / w : null;

    private static double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.0;

    // "individual_pro" -> "Individual pro"
    private static string SentenceCase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string words = value.Trim().Replace('_', ' ');
        return string.Concat(words[..1].ToUpperInvariant(), words[1..]);
    }

    /// <summary>Tolerant JSON lookups: wrong kinds and missing fields read as null.</summary>
    private static class JsonPick
    {
        public static JsonDocument? Open(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonDocument.Parse(json); }
            catch (JsonException) { return null; }
        }

        public static JsonElement? Child(JsonElement parent, string name) =>
            parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

        public static string? Text(JsonElement parent, string name) =>
            parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        public static double? Number(JsonElement parent, string name)
        {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var v)) return null;
            double d = 0;
            bool read = v.ValueKind switch
            {
                JsonValueKind.Number => v.TryGetDouble(out d),
                JsonValueKind.String => double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d),
                _ => false
            };
            return read && double.IsFinite(d) ? d : null;
        }

        /// <summary>An ISO time, or unix seconds / milliseconds as a number or a digit string.</summary>
        public static DateTimeOffset? Moment(JsonElement parent, string name)
        {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out var n) ? FromEpoch(n) : null;
            if (v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString())) return null;
            string s = v.GetString()!.Trim();
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits)) return FromEpoch(digits);
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
        }

        // Values above 10^11 cannot be seconds in any plausible year, so they are milliseconds.
        public static DateTimeOffset? FromEpoch(long value)
        {
            if (value <= 0) return null;
            try { return value > 100_000_000_000L ? DateTimeOffset.FromUnixTimeMilliseconds(value) : DateTimeOffset.FromUnixTimeSeconds(value); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
    }

    /// <summary>
    /// A decoded protobuf message: just enough of the wire format (varint, 64-bit, length-delimited, 32-bit) to walk nested
    /// messages. Decoding stops quietly at the first malformed or unsupported field.
    /// </summary>
    private sealed class Proto
    {
        private readonly List<(int Field, int Wire, ulong Number, byte[]? Payload)> fields = new();

        public static Proto Parse(byte[] data)
        {
            var message = new Proto();
            int pos = 0;
            while (pos < data.Length)
            {
                if (!TryVarint(data, ref pos, out ulong key)) break;
                int field = (int)(key >> 3), wire = (int)(key & 0x7);
                if (field <= 0) break;
                if (wire == 0)
                {
                    if (!TryVarint(data, ref pos, out ulong value)) break;
                    message.fields.Add((field, 0, value, null));
                }
                else if (wire == 1)
                {
                    if (data.Length - pos < 8) break;
                    message.fields.Add((field, 1, BitConverter.ToUInt64(data, pos), null));
                    pos += 8;
                }
                else if (wire == 5)
                {
                    if (data.Length - pos < 4) break;
                    message.fields.Add((field, 5, BitConverter.ToUInt32(data, pos), null));
                    pos += 4;
                }
                else if (wire == 2)
                {
                    if (!TryVarint(data, ref pos, out ulong length) || length > (ulong)(data.Length - pos)) break;
                    message.fields.Add((field, 2, 0, data.AsSpan(pos, (int)length).ToArray()));
                    pos += (int)length;
                }
                else break; // groups and unknown wire types are not used by the messages read here
            }
            return message;
        }

        public byte[]? Bytes(int field) => fields.FirstOrDefault(f => f.Field == field && f.Wire == 2).Payload;

        public Proto? Message(int field) => Bytes(field) is { } payload ? Parse(payload) : null;

        public IEnumerable<Proto> Messages(int field) => fields.Where(f => f.Field == field && f.Wire == 2).Select(f => Parse(f.Payload!));

        public ulong? Varint(int field) => fields.Where(f => f.Field == field && f.Wire == 0).Select(f => (ulong?)f.Number).FirstOrDefault();

        public uint? Fixed32(int field) => fields.Where(f => f.Field == field && f.Wire == 5).Select(f => (uint?)f.Number).FirstOrDefault();

        private static bool TryVarint(byte[] data, ref int pos, out ulong value)
        {
            value = 0;
            for (int shift = 0; shift < 64 && pos < data.Length; shift += 7)
            {
                byte b = data[pos++];
                value |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80) return true;
            }
            return false;
        }
    }
}
