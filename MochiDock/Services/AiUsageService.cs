using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>How far a provider reading can be trusted. Anything but Ok comes with a status line and usually a hint.</summary>
public enum AiProviderState { Ok, SignedOut, Expired, Disabled, Unreadable, Offline }

/// <summary>One bar on the AI card: the limit's name, the spent share (0..1) and when it renews, as text.</summary>
public record AiUsageWindow(string Label, double UsedRatio, string ResetsInText);

/// <summary>What the AI card shows for one tool. Windows are empty whenever no real figure is known.</summary>
public record AiProviderSnapshot(string ProviderId, string ProviderName, string Plan, string MaskedAccount, List<AiUsageWindow> Windows, string StatusMessage, bool IsError, AiProviderState State = AiProviderState.Ok, string Hint = "");

/// <summary>
/// Reads plan usage from the AI coding tools signed in on this PC, using each tool's own saved sign-in. Sign-ins are only
/// read, never renewed, stored or logged. Reads happen while the AI card is on screen (or, with alerts on, every few minutes),
/// and each provider is asked at most once per spacing interval.
/// </summary>
public class AiUsageService : IDisposable
{
    private static readonly TimeSpan BusyToolSpacing = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CalmToolSpacing = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ManualSpacing = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan VisiblePeriod = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan BackgroundPeriod = TimeSpan.FromMinutes(5);

    private const string LiveStatus = "Live";
    private const string NotSignedInStatus = "Not signed in";
    private const string LoginExpiredStatus = "Login expired";
    private const string RetryHint = "MochiDock will ask again in a few minutes.";

    /// <summary>One tool MochiDock knows how to read.</summary>
    private sealed record Provider(string Id, string Name, TimeSpan Spacing, Func<CancellationToken, Task<AiProviderSnapshot?>> Read);

    /// <summary>What is remembered per tool between reads.</summary>
    private sealed class Memory
    {
        public DateTimeOffset AskedAt;
        public AiProviderSnapshot? Latest;
        public AiProviderSnapshot? LastHealthy;
        public DateTimeOffset LastHealthyAt;
    }

    private readonly HttpClient _http;
    private readonly IReadOnlyList<Provider> _providers;
    private readonly Dictionary<string, Memory> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly AiUsageAlertPolicy _alertPolicy = new();
    private readonly object _gate = new();
    private System.Threading.Timer? _ticker;
    private bool _cardVisible;
    private bool _alertsOn;
    private bool _disposed;

    public event Action? Refreshed;

    /// <summary>Raised (possibly on a background thread) when a provider newly falls under the low-usage limit.</summary>
    public event Action<AiUsageAlertReading>? LowUsageDetected;

    /// <summary>While on, reads continue in the background (every 5 minutes) so alerts do not need the AI card open.</summary>
    public bool AlertsEnabled
    {
        get { lock (_gate) return _alertsOn; }
        set
        {
            lock (_gate)
            {
                if (_alertsOn == value) return;
                _alertsOn = value;
                if (!value) _alertPolicy.Reset();
                ApplyScheduleLocked();
            }
        }
    }

    public List<AiProviderSnapshot> Snapshots { get; private set; } = new();
    public bool IsRefreshing { get; private set; }
    public bool HasRefreshed { get; private set; }
    public string LastUpdatedText { get; private set; } = "";

    /// <summary>Providers the user hid; they are not read at all.</summary>
    public List<string> HiddenProviders { get; set; } = new();

    public AiUsageService(HttpClient? client = null)
    {
        _http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppUserAgent.Value);
        _providers = new Provider[]
        {
            new("antigravity", "Antigravity IDE", CalmToolSpacing, ReadAntigravityAsync),
            new("codex", "Codex", BusyToolSpacing, ReadCodexAsync),
            new("claude", "Claude Code", BusyToolSpacing, ReadClaudeAsync),
            new("cursor", "Cursor", CalmToolSpacing, ReadCursorAsync),
            new("gemini", "Gemini CLI", CalmToolSpacing, ReadGeminiAsync),
            new("copilot", "GitHub Copilot", CalmToolSpacing, ReadCopilotAsync),
        };
    }

    // ---- scheduling ---------------------------------------------------------------------------------------------------

    /// <summary>The AI card reports whether it is on screen; reading follows it.</summary>
    public void SetVisible(bool visible)
    {
        lock (_gate)
        {
            if (_disposed || _cardVisible == visible) return;
            _cardVisible = visible;
            ApplyScheduleLocked();
        }
    }

    // Card on screen: soon, then every 2 minutes. Card hidden with alerts on: after 30 s, then every 5 minutes. Otherwise idle.
    private void ApplyScheduleLocked()
    {
        if (_disposed) return;
        (TimeSpan First, TimeSpan Every)? plan = _cardVisible ? (TimeSpan.FromSeconds(1), VisiblePeriod)
            : _alertsOn ? (TimeSpan.FromSeconds(30), BackgroundPeriod)
            : null;
        if (plan is not { } p)
        {
            _ticker?.Dispose();
            _ticker = null;
            return;
        }
        _ticker ??= new System.Threading.Timer(_ => _ = RefreshAllAsync(force: false));
        _ticker.Change(p.First, p.Every);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _ticker?.Dispose();
            _ticker = null;
        }
        _shutdown.Cancel();
        GC.SuppressFinalize(this);
    }

    // ---- refreshing ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads every visible provider whose last read is older than its spacing (15 s for a manual refresh) and publishes the
    /// results, most headroom first. Concurrent calls are dropped while one runs.
    /// </summary>
    public async Task RefreshAllAsync(List<string>? hiddenProviders = null, bool force = true)
    {
        lock (_gate)
        {
            if (_disposed || IsRefreshing) return;
            IsRefreshing = true;
        }
        var hidden = new HashSet<string>(hiddenProviders ?? HiddenProviders ?? new(), StringComparer.OrdinalIgnoreCase);
        var token = _shutdown.Token;
        bool publish = false;
        try
        {
            var reads = _providers.Where(p => !hidden.Contains(p.Id)).Select(p => ReadOrReuseAsync(p, force, token));
            var found = (await Task.WhenAll(reads)).OfType<AiProviderSnapshot>().ToList();
            if (token.IsCancellationRequested) return;

            Snapshots = AiUsageParsing.RankByHeadroom(found, s => s.ProviderId, s => s.Windows.Select(w => w.UsedRatio).ToList());
            HasRefreshed = true;
            LastUpdatedText = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
            publish = true;
            RaiseLowUsageAlerts(Snapshots);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Warn("AiUsage", ex);
            publish = true;
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_gate) IsRefreshing = false;
            if (publish && !token.IsCancellationRequested) Refreshed?.Invoke();
        }
    }

    // Returns the remembered reading when it is recent enough, otherwise reads. Readers report trouble as a state; a lost
    // connection shows the last healthy reading marked offline, and anything unexpected leaves the provider out.
    private async Task<AiProviderSnapshot?> ReadOrReuseAsync(Provider provider, bool manual, CancellationToken token)
    {
        var spacing = manual ? ManualSpacing : provider.Spacing;
        Memory memory;
        lock (_gate)
        {
            if (_memory.TryGetValue(provider.Id, out var known)) memory = known;
            else _memory[provider.Id] = memory = new Memory();
            if (memory.AskedAt != default && DateTimeOffset.UtcNow - memory.AskedAt < spacing) return memory.Latest;
        }

        AiProviderSnapshot? reading;
        try
        {
            reading = await provider.Read(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            reading = OfflineCopy(memory);
        }
        catch (Exception ex)
        {
            AppLog.Warn("AiUsage", $"{provider.Id} reader failed: {ex.GetType().Name}");
            reading = null;
        }

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            memory.AskedAt = now;
            memory.Latest = reading;
            if (reading is { State: AiProviderState.Ok, IsError: false, Windows.Count: > 0 })
            {
                memory.LastHealthy = reading;
                memory.LastHealthyAt = now;
            }
        }
        return reading;
    }

    private AiProviderSnapshot? OfflineCopy(Memory memory)
    {
        lock (_gate)
        {
            if (memory.LastHealthy is not { } healthy) return null;
            string when = memory.LastHealthyAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            return healthy with { StatusMessage = $"Offline, as of {when}", IsError = true, State = AiProviderState.Offline };
        }
    }

    private void RaiseLowUsageAlerts(IEnumerable<AiProviderSnapshot> snapshots)
    {
        if (!AlertsEnabled) return;
        // Only fresh, healthy readings count: an offline copy must not alert again, and Antigravity's figures are estimates.
        var readings = new List<AiUsageAlertReading>();
        foreach (var s in snapshots)
        {
            if (s.State != AiProviderState.Ok || s.Windows.Count == 0 || s.ProviderId == "antigravity") continue;
            var mostSpent = s.Windows.MaxBy(w => w.UsedRatio)!;
            double left = Math.Round(100.0 - Math.Clamp(mostSpent.UsedRatio * 100.0, 0, 100), 1);
            readings.Add(new AiUsageAlertReading(s.ProviderId, s.ProviderName, mostSpent.Label, left));
        }
        foreach (var alert in _alertPolicy.Evaluate(readings))
        {
            try { LowUsageDetected?.Invoke(alert); }
            catch (Exception ex) { AppLog.Warn("AiUsage", ex); }
        }
    }

    // ---- building snapshots ---------------------------------------------------------------------------------------------

    private static AiProviderSnapshot Healthy(string id, string name, string plan, string account, IEnumerable<QuotaGauge> gauges, string status = LiveStatus) =>
        new(id, name, plan, account, Bars(gauges), status, false);

    private static AiProviderSnapshot Trouble(string id, string name, AiProviderState state, string status, string hint, string plan = "", string account = "") =>
        new(id, name, plan, account, new List<AiUsageWindow>(), status, true, state, hint);

    private static List<AiUsageWindow> Bars(IEnumerable<QuotaGauge> gauges)
    {
        var now = DateTimeOffset.Now;
        return gauges.Select(g => new AiUsageWindow(g.Label, g.Used, AiUsageParsing.RenewalText(g.RenewsAt, now))).ToList();
    }

    // Answers that are not a usable success. Null means the body can be parsed.
    private static AiProviderSnapshot? RejectedAnswer(string id, string name, HttpStatusCode code, string expiredHint, string plan = "", string account = "")
    {
        if (code is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Trouble(id, name, AiProviderState.Expired, LoginExpiredStatus, expiredHint, plan, account);
        if ((int)code >= 400)
            return Trouble(id, name, AiProviderState.Unreadable, $"Error {(int)code}", RetryHint, plan, account);
        return null;
    }

    private async Task<(HttpStatusCode Code, string Body)> FetchAsync(HttpRequestMessage request, CancellationToken token)
    {
        using (request)
        using (var response = await _http.SendAsync(request, token))
            return (response.StatusCode, await response.Content.ReadAsStringAsync(token));
    }

    private static HttpRequestMessage Get(string url, string? authorization)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (authorization != null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private static HttpRequestMessage PostJson(string url, string json, string? authorization = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (authorization != null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return request;
    }

    private static string HomeFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string RoamingFolder => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string LocalFolder => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static JsonDocument? LoadJson(string path)
    {
        try { return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null; }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static string? StringAt(JsonElement element, params string[] path)
    {
        foreach (var step in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(step, out element)) return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static long? LongAt(JsonElement element, params string[] path)
    {
        foreach (var step in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(step, out element)) return null;
        }
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var n) ? n : null;
    }

    /// <summary>"jane.doe@example.com" -> "ja***@example.com"; anything that is not an address becomes "".</summary>
    private static string HideAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "";
        int at = address.IndexOf('@');
        return at < 1 ? "" : string.Concat(address.AsSpan(0, Math.Min(2, at)), "***", address.AsSpan(at));
    }

    private static string Capital(string? word)
    {
        if (string.IsNullOrWhiteSpace(word)) return "";
        var w = word.Trim();
        return char.ToUpperInvariant(w[0]) + w[1..];
    }

    private static string Clock(DateTimeOffset moment, DateTimeOffset now)
    {
        var local = moment.ToLocalTime();
        return local.ToString(local.Date == now.ToLocalTime().Date ? "HH:mm" : "MMM d HH:mm", CultureInfo.InvariantCulture);
    }

    private static string Duration(TimeSpan age) =>
        age.TotalDays >= 2 ? $"{(int)age.TotalDays}d" : age.TotalHours >= 1 ? $"{(int)age.TotalHours}h" : $"{Math.Max(1, (int)age.TotalMinutes)}m";

    // ---- Claude Code (and the Claude desktop app) -----------------------------------------------------------------------

    private const string ClaudeId = "claude", ClaudeName = "Claude Code";
    private const string ClaudeSignInHint = "Sign in to Claude Code or the Claude app to see your limits.";
    private const string ClaudeRenewHint = "Start Claude Code or the Claude app; they renew their login (MochiDock never does) and the app records usage every 15 minutes.";
    private const string ClaudeAppOldHint = "Start the Claude app for a newer reading; it records usage only while it runs.";
    private static readonly TimeSpan ClaudeAppCurrent = TimeSpan.FromMinutes(90);
    private static readonly TimeSpan ClaudeAppOldest = TimeSpan.FromDays(7);
    private static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);

    private async Task<AiProviderSnapshot?> ReadClaudeAsync(CancellationToken token)
    {
        string configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(HomeFolder, ".claude");
        string credentialsPath = Path.Combine(configDir, ".credentials.json");
        string profilePath = Path.Combine(HomeFolder, ".claude.json");
        string account = ClaudeAccount(profilePath);

        if (!File.Exists(credentialsPath))
        {
            // Some people only use the Claude app, which keeps its own usage history.
            return await ClaudeAppReadingAsync("", account, token)
                ?? (File.Exists(profilePath) || Directory.Exists(configDir) ? Trouble(ClaudeId, ClaudeName, AiProviderState.SignedOut, NotSignedInStatus, ClaudeSignInHint) : null);
        }

        string accessToken, plan;
        DateTimeOffset? expiresAt = null;
        try
        {
            using var creds = JsonDocument.Parse(await File.ReadAllTextAsync(credentialsPath, token));
            var root = creds.RootElement;
            accessToken = StringAt(root, "claudeAiOauth", "accessToken") ?? "";
            plan = Capital(StringAt(root, "claudeAiOauth", "subscriptionType"));
            if (LongAt(root, "claudeAiOauth", "expiresAt") is { } ms && ms > 0) expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            return Trouble(ClaudeId, ClaudeName, AiProviderState.Unreadable, "Login unreadable", "Claude Code's saved login file could not be read.");
        }

        if (string.IsNullOrWhiteSpace(accessToken))
            return await ClaudeAppReadingAsync(plan, account, token)
                ?? Trouble(ClaudeId, ClaudeName, AiProviderState.SignedOut, NotSignedInStatus, ClaudeSignInHint, plan, account);
        if (expiresAt is { } exp && exp <= DateTimeOffset.UtcNow)
            return await ClaudeAppReadingAsync(plan, account, token)
                ?? Trouble(ClaudeId, ClaudeName, AiProviderState.Expired, LoginExpiredStatus, ClaudeRenewHint, plan, account);

        var request = Get("https://api.anthropic.com/api/oauth/usage", "Bearer " + accessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        var (code, body) = await FetchAsync(request, token);
        if (code is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return await ClaudeAppReadingAsync(plan, account, token)
                ?? Trouble(ClaudeId, ClaudeName, AiProviderState.Expired, LoginExpiredStatus, ClaudeRenewHint, plan, account);
        if (RejectedAnswer(ClaudeId, ClaudeName, code, ClaudeRenewHint, plan, account) is { } rejected) return rejected;

        var limits = AiUsageParsing.ReadClaudeLimits(body);
        return limits.Count == 0
            ? Trouble(ClaudeId, ClaudeName, AiProviderState.Unreadable, "No limits reported", "Anthropic's answer listed no limits for this account.", plan, account)
            : Healthy(ClaudeId, ClaudeName, plan, account, limits);
    }

    private static string ClaudeAccount(string profilePath)
    {
        using var profile = LoadJson(profilePath);
        return profile is null ? "" : HideAddress(StringAt(profile.RootElement, "oauthAccount", "emailAddress"));
    }

    /// <summary>
    /// The Claude desktop app writes plain usage percentages (no tokens) to plan-usage-history.json about every 15 minutes
    /// while it runs. The newest copy is used. Recent samples count as live; older ones (up to a week) are shown dated and
    /// as Offline so they never raise alerts, without the 5-hour figure once that window has surely passed.
    /// </summary>
    private static async Task<AiProviderSnapshot?> ClaudeAppReadingAsync(string plan, string account, CancellationToken token)
    {
        var path = NewestClaudeAppHistory();
        if (path is null) return null;
        string json;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            json = await reader.ReadToEndAsync(token);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("AiUsage", e);
            return null;
        }

        var now = DateTimeOffset.Now;
        if (AiUsageParsing.ReadClaudeAppHistory(json, now, ClaudeAppOldest) is not { } sample) return null;
        var age = now - sample.TakenAt;
        if (age <= ClaudeAppCurrent)
        {
            var gauges = sample.Gauges();
            return gauges.Count == 0 ? null : Healthy(ClaudeId, ClaudeName, plan, account, gauges, "Live, via Claude app");
        }

        var older = sample.Gauges(includeFiveHour: age <= FiveHours);
        if (older.Count == 0) return null;
        return new AiProviderSnapshot(ClaudeId, ClaudeName, plan, account, Bars(older), $"Claude app, {Clock(sample.TakenAt, now)}", false, AiProviderState.Offline, ClaudeAppOldHint);
    }

    private static string? NewestClaudeAppHistory()
    {
        const string fileName = "plan-usage-history.json";
        var places = new List<string>
        {
            Path.Combine(RoamingFolder, "Claude", fileName),
            Path.Combine(RoamingFolder, "Claude-3p", fileName),
        };
        try
        {
            string dataRoot = Path.Combine(LocalFolder, "Claude-Data");
            if (Directory.Exists(dataRoot)) places.AddRange(Directory.GetDirectories(dataRoot).Select(d => Path.Combine(d, fileName)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return places.Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    // ---- Codex --------------------------------------------------------------------------------------------------------

    private const string CodexId = "codex", CodexName = "Codex";
    private const int RolloutTailBytes = 64 * 1024;

    private async Task<AiProviderSnapshot?> ReadCodexAsync(CancellationToken token)
    {
        string codexDir = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(HomeFolder, ".codex");
        string authPath = Path.Combine(codexDir, "auth.json");
        string sessionsDir = Path.Combine(codexDir, "sessions");
        if (!File.Exists(authPath) && !Directory.Exists(sessionsDir)) return null;

        string accessToken = "", accountId = "";
        using (var auth = LoadJson(authPath))
        {
            if (auth != null)
            {
                accessToken = StringAt(auth.RootElement, "tokens", "access_token") ?? "";
                accountId = StringAt(auth.RootElement, "tokens", "account_id") ?? "";
            }
        }

        // The ChatGPT usage service first; if it cannot answer, the newest rate-limit event Codex wrote to its session logs.
        var reading = await CodexOnlineAsync(accessToken, accountId, token);
        if (reading != null) return Healthy(CodexId, CodexName, reading.Plan, "", reading.Gauges);

        reading = await CodexFromLogsAsync(sessionsDir, token);
        if (reading != null) return Healthy(CodexId, CodexName, reading.Plan, "", reading.Gauges, "Live, from session log");

        return new AiProviderSnapshot(CodexId, CodexName, "", "", new List<AiUsageWindow>(), "No usage yet", false);
    }

    private async Task<PlanReading?> CodexOnlineAsync(string accessToken, string accountId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return null;
        var request = Get("https://chatgpt.com/backend-api/wham/usage", "Bearer " + accessToken);
        if (!string.IsNullOrWhiteSpace(accountId)) request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", accountId);
        try
        {
            var (code, body) = await FetchAsync(request, token);
            return (int)code is >= 200 and < 300 ? AiUsageParsing.ReadCodexUsage(body) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<PlanReading?> CodexFromLogsAsync(string sessionsDir, CancellationToken token)
    {
        if (!Directory.Exists(sessionsDir)) return null;
        List<FileInfo> newest;
        try
        {
            newest = new DirectoryInfo(sessionsDir).EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc).Take(4).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }

        foreach (var file in newest)
        {
            string tail;
            try
            {
                await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(-Math.Min(stream.Length, RolloutTailBytes), SeekOrigin.End);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                tail = await reader.ReadToEndAsync(token);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }

            var lines = tail.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (!line.Contains("token_count", StringComparison.Ordinal) || !line.Contains("rate_limits", StringComparison.Ordinal)) continue;
                if (AiUsageParsing.ReadCodexRolloutLine(line) is { } reading) return reading;
            }
        }
        return null;
    }

    // ---- Cursor -------------------------------------------------------------------------------------------------------

    private const string CursorId = "cursor", CursorName = "Cursor";
    private const string CursorRenewHint = "Start Cursor to renew its login.";
    private const string SealedTokenPrefix = "cursor-safe-storage:v1:";

    private async Task<AiProviderSnapshot?> ReadCursorAsync(CancellationToken token)
    {
        string cursorDir = Path.Combine(RoamingFolder, "Cursor");
        string databasePath = Path.Combine(cursorDir, "User", "globalStorage", "state.vscdb");
        if (!File.Exists(databasePath)) return null;

        string? accessToken, email, membership;
        try
        {
            // Large and held open by Cursor: three keys are read in place, never copied.
            using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;");
            await connection.OpenAsync(token);
            accessToken = await StoredValueAsync(connection, "cursorAuth/accessToken", token);
            email = await StoredValueAsync(connection, "cursorAuth/cachedEmail", token);
            membership = await StoredValueAsync(connection, "cursorAuth/stripeMembershipType", token);
        }
        catch (SqliteException)
        {
            return Trouble(CursorId, CursorName, AiProviderState.Unreadable, "Database busy", "Cursor is holding its database right now. " + RetryHint);
        }

        string account = HideAddress(email);
        string plan = Capital(membership);
        accessToken = accessToken?.Trim();
        if (string.IsNullOrEmpty(accessToken))
            return Trouble(CursorId, CursorName, AiProviderState.SignedOut, NotSignedInStatus, "Sign in to Cursor to see your limits.", plan, account);

        if (accessToken.StartsWith(SealedTokenPrefix, StringComparison.Ordinal))
        {
            accessToken = OpenSealedSecret(accessToken[SealedTokenPrefix.Length..], Path.Combine(cursorDir, "Local State"));
            if (string.IsNullOrEmpty(accessToken))
                return Trouble(CursorId, CursorName, AiProviderState.Unreadable, "Login locked", "Cursor's protected login could not be opened for this Windows user.", plan, account);
        }

        var session = AiUsageParsing.ReadCursorSession(accessToken);
        if (session is null || !AiUsageParsing.IsCookieSafeToken(accessToken))
            return Trouble(CursorId, CursorName, AiProviderState.Unreadable, "Unknown login format", "Cursor's saved login is not in a form MochiDock understands.", plan, account);
        if (session.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
            return Trouble(CursorId, CursorName, AiProviderState.Expired, LoginExpiredStatus, CursorRenewHint, plan, account);

        var request = Get("https://cursor.com/api/usage-summary", null);
        request.Headers.TryAddWithoutValidation("Cookie", $"WorkosCursorSessionToken={session.UserId}%3A%3A{accessToken}");
        var (code, body) = await FetchAsync(request, token);
        if (RejectedAnswer(CursorId, CursorName, code, CursorRenewHint, plan, account) is { } rejected) return rejected;

        if (AiUsageParsing.ReadCursorSummary(body) is not { } reading)
            return Trouble(CursorId, CursorName, AiProviderState.Unreadable, "No limits reported", "Cursor's answer had no usage in it.", plan, account);
        return Healthy(CursorId, CursorName, reading.Plan.Length > 0 ? reading.Plan : plan, account, reading.Gauges);
    }

    private static async Task<string?> StoredValueAsync(SqliteConnection connection, string key, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM ItemTable WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return (await command.ExecuteScalarAsync(token))?.ToString();
    }

    /// <summary>
    /// Opens a secret sealed with Electron safeStorage on Windows. Format "v10" | 12-byte nonce | AES-256-GCM ciphertext | 16-byte tag,
    /// keyed by Local State's os_crypt.encrypted_key ("DPAPI" + a blob only this Windows user can open). Without "v10" the whole
    /// value is a DPAPI blob. Returns null when anything does not fit.
    /// </summary>
    internal static string? OpenSealedSecret(string sealedBase64, string localStatePath)
    {
        try
        {
            var sealedBytes = AiUsageParsing.DecodeBase64Url(sealedBase64);
            if (sealedBytes is not { Length: > 0 }) return null;
            if (!sealedBytes.AsSpan().StartsWith("v10"u8))
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(sealedBytes, null, DataProtectionScope.CurrentUser));

            const int NonceSize = 12, TagSize = 16, Header = 3;
            if (sealedBytes.Length < Header + NonceSize + TagSize) return null;
            if (!File.Exists(localStatePath) || new FileInfo(localStatePath).Length > 1024 * 1024) return null;
            using var state = LoadJson(localStatePath);
            var wrappedKey = state is null ? null : AiUsageParsing.DecodeBase64Url(StringAt(state.RootElement, "os_crypt", "encrypted_key") ?? "");
            if (wrappedKey is null || wrappedKey.Length <= 5 || !wrappedKey.AsSpan().StartsWith("DPAPI"u8)) return null;
            byte[] key = ProtectedData.Unprotect(wrappedKey.AsSpan(5).ToArray(), null, DataProtectionScope.CurrentUser);

            var body = sealedBytes.AsSpan(Header);
            var plain = new byte[body.Length - NonceSize - TagSize];
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(body[..NonceSize], body[NonceSize..^TagSize], body[^TagSize..], plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // ---- Gemini CLI ---------------------------------------------------------------------------------------------------

    private const string GeminiId = "gemini", GeminiName = "Gemini CLI";
    private const string GeminiSignInHint = "Sign in with the Gemini CLI to see your limits.";
    private const string GeminiRenewHint = "Run the Gemini CLI to renew its login.";
    private const string GeminiPersonalHint = "Google reports Gemini CLI limits only for Workspace and enterprise accounts, not personal ones.";
    private const string CodeAssistBase = "https://cloudcode-pa.googleapis.com/v1internal";

    private async Task<AiProviderSnapshot?> ReadGeminiAsync(CancellationToken token)
    {
        string geminiDir = Path.Combine(Environment.GetEnvironmentVariable("GEMINI_CLI_HOME") ?? HomeFolder, ".gemini");
        string credsPath = Path.Combine(geminiDir, "oauth_creds.json");
        string settingsPath = Path.Combine(geminiDir, "settings.json");

        // API keys, Vertex and Cloud Shell sign-ins carry no plan limits to read.
        using (var settings = LoadJson(settingsPath))
        {
            string method = settings is null ? "" : StringAt(settings.RootElement, "security", "auth", "selectedType") ?? "";
            if (new[] { "api-key", "vertex", "cloud-shell" }.Any(kind => method.Contains(kind, StringComparison.OrdinalIgnoreCase))) return null;
        }
        if (!File.Exists(credsPath))
            return File.Exists(settingsPath) ? Trouble(GeminiId, GeminiName, AiProviderState.SignedOut, NotSignedInStatus, GeminiSignInHint) : null;

        string accessToken;
        DateTimeOffset? expiresAt = null;
        try
        {
            using var creds = JsonDocument.Parse(await File.ReadAllTextAsync(credsPath, token));
            accessToken = StringAt(creds.RootElement, "access_token") ?? "";
            if (LongAt(creds.RootElement, "expiry_date") is { } ms && ms > 0) expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            return Trouble(GeminiId, GeminiName, AiProviderState.Unreadable, "Login unreadable", "The Gemini CLI's saved login file could not be read.");
        }
        if (string.IsNullOrWhiteSpace(accessToken)) return Trouble(GeminiId, GeminiName, AiProviderState.SignedOut, NotSignedInStatus, GeminiSignInHint);
        if (expiresAt is { } exp && exp <= DateTimeOffset.UtcNow) return Trouble(GeminiId, GeminiName, AiProviderState.Expired, LoginExpiredStatus, GeminiRenewHint);

        string bearer = "Bearer " + accessToken;
        var (loadCode, loadBody) = await FetchAsync(PostJson(CodeAssistBase + ":loadCodeAssist",
            """{"metadata":{"ideType":"IDE_UNSPECIFIED","platform":"PLATFORM_UNSPECIFIED","pluginType":"GEMINI"}}""", bearer), token);
        if (AiUsageParsing.IsPersonalGeminiRefusal(loadBody))
            return Trouble(GeminiId, GeminiName, AiProviderState.Disabled, "Unavailable", GeminiPersonalHint);
        if (RejectedAnswer(GeminiId, GeminiName, loadCode, GeminiRenewHint) is { } loadRejected) return loadRejected;

        var account = AiUsageParsing.ReadCodeAssistAccount(loadBody);
        string tier = account.Tier ?? "";
        if (string.IsNullOrEmpty(account.ProjectId))
            return Trouble(GeminiId, GeminiName, AiProviderState.Unreadable, "No Code Assist project", "Google reported no Code Assist project for this account.", tier);

        var (quotaCode, quotaBody) = await FetchAsync(PostJson(CodeAssistBase + ":retrieveUserQuota",
            JsonSerializer.Serialize(new { project = account.ProjectId }), bearer), token);
        if (AiUsageParsing.IsPersonalGeminiRefusal(quotaBody))
            return Trouble(GeminiId, GeminiName, AiProviderState.Disabled, "Unavailable", GeminiPersonalHint, tier);
        if (RejectedAnswer(GeminiId, GeminiName, quotaCode, GeminiRenewHint, tier) is { } quotaRejected) return quotaRejected;

        var gauges = AiUsageParsing.ReadGeminiQuota(quotaBody);
        return gauges.Count == 0
            ? Trouble(GeminiId, GeminiName, AiProviderState.Unreadable, "No limits reported", "Google listed no model limits for this account.", tier)
            : Healthy(GeminiId, GeminiName, tier, "", gauges);
    }

    // ---- GitHub Copilot ---------------------------------------------------------------------------------------------------

    private const string CopilotId = "copilot", CopilotName = "GitHub Copilot";

    private async Task<AiProviderSnapshot?> ReadCopilotAsync(CancellationToken token)
    {
        string copilotDir = Path.Combine(LocalFolder, "github-copilot");
        var files = new[] { "apps.json", "hosts.json" }.Select(f => Path.Combine(copilotDir, f)).Where(File.Exists).ToList();
        if (files.Count == 0) return null;

        string? oauthToken = null;
        foreach (var file in files)
        {
            using var doc = LoadJson(file);
            if (doc?.RootElement.ValueKind != JsonValueKind.Object) continue;
            oauthToken = doc.RootElement.EnumerateObject()
                .Select(entry => StringAt(entry.Value, "oauth_token"))
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (oauthToken != null) break;
        }
        if (oauthToken is null)
            return Trouble(CopilotId, CopilotName, AiProviderState.SignedOut, NotSignedInStatus, "Sign in to GitHub Copilot in your editor to see your limits.");

        var (code, body) = await FetchAsync(Get("https://api.github.com/copilot_internal/user", "token " + oauthToken), token);
        if (RejectedAnswer(CopilotId, CopilotName, code, "Sign in to GitHub Copilot again in your editor.") is { } rejected) return rejected;

        if (AiUsageParsing.ReadCopilot(body) is not { } reading)
            return Trouble(CopilotId, CopilotName, AiProviderState.Unreadable, "Answer unreadable", "GitHub's answer could not be read.");
        return Healthy(CopilotId, CopilotName, reading.Plan, "", reading.Gauges, reading.Gauges.Count > 0 ? LiveStatus : "No limits reported");
    }

    // ---- Antigravity IDE ----------------------------------------------------------------------------------------------------

    private const string AntigravityId = "antigravity", AntigravityName = "Antigravity IDE", AntigravityAccount = "This PC";
    private static readonly TimeSpan AntigravityStaleAfter = TimeSpan.FromMinutes(30);

    // The card leaves out the shared Claude/GPT-OSS pool to stay compact.
    private const bool ShowAntigravityPartnerPool = false;

    private async Task<AiProviderSnapshot?> ReadAntigravityAsync(CancellationToken token)
    {
        string agentDir = Path.Combine(HomeFolder, ".gemini", "antigravity");
        string statePath = Path.Combine(RoamingFolder, "Antigravity IDE", "User", "globalStorage", "state.vscdb");
        if (!Directory.Exists(agentDir) && !File.Exists(statePath)) return null;

        // While the IDE runs, its local language server answers with the figures the IDE itself shows.
        if (await AntigravityLiveAsync(agentDir, token) is { } live)
            return Healthy(AntigravityId, AntigravityName, live.Plan, AntigravityAccount, live.Gauges);

        // Otherwise use what the IDE last saved; it only updates that while it runs.
        var saved = new List<QuotaGauge>();
        if (File.Exists(statePath))
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={statePath};Mode=ReadOnly;");
                await connection.OpenAsync(token);
                var stored = await StoredValueAsync(connection, "antigravityUnifiedStateSync.userStatus", token);
                saved = AiUsageParsing.ReadAntigravityStoredStatus(stored, DateTimeOffset.UtcNow, ShowAntigravityPartnerPool);
            }
            catch (SqliteException e) { AppLog.Warn("AiUsage", e); }
        }
        if (saved.Count == 0)
            return Trouble(AntigravityId, AntigravityName, AiProviderState.Unreadable, "No quota found", "Start Antigravity once so it saves its quota.", "", AntigravityAccount);

        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(statePath);
        return age > AntigravityStaleAfter
            ? new AiProviderSnapshot(AntigravityId, AntigravityName, "", AntigravityAccount, Bars(saved), $"Synced {Duration(age)} ago", false, AiProviderState.Ok, "Start Antigravity to update its quota.")
            : Healthy(AntigravityId, AntigravityName, "", AntigravityAccount, saved);
    }

    // The language server announces itself in daemon/hub_<port>.json (pid, httpPort, csrfToken) and only answers 127.0.0.1
    // requests that carry that token. The token comes from the user's own IDE files, goes nowhere else and is never logged.
    private async Task<AntigravityLiveReading?> AntigravityLiveAsync(string agentDir, CancellationToken token)
    {
        string daemonDir = Path.Combine(agentDir, "daemon");
        if (!Directory.Exists(daemonDir)) return null;
        List<FileInfo> hubs;
        try { hubs = new DirectoryInfo(daemonDir).GetFiles("hub_*.json").OrderByDescending(f => f.LastWriteTimeUtc).Take(3).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }

        foreach (var hub in hubs)
        {
            int port;
            string csrf;
            using (var doc = LoadJson(hub.FullName))
            {
                if (doc is null) continue;
                var root = doc.RootElement;
                if (LongAt(root, "httpPort") is not { } p || p is < 1 or > 65535) continue;
                if (StringAt(root, "csrfToken") is not { Length: > 0 } t) continue;
                // A file left behind by an IDE that has exited points at nothing.
                if (LongAt(root, "pid") is { } pid && !IsRunning(pid)) continue;
                (port, csrf) = ((int)p, t);
            }

            var statusBody = await AskLanguageServerAsync(port, csrf, "GetUserStatus", """{"metadata":{"ideName":"antigravity","extensionName":"antigravity"}}""", token);
            if (statusBody is null || AiUsageParsing.ReadAntigravityLive(statusBody, DateTimeOffset.UtcNow, ShowAntigravityPartnerPool) is not { } live) continue;

            // The quota summary has the 7-day and 5-hour limits of the IDE's usage panel; per-model figures are the fallback.
            var summaryBody = await AskLanguageServerAsync(port, csrf, "RetrieveUserQuotaSummary", "{}", token);
            var summary = summaryBody is null ? new List<QuotaGauge>() : AiUsageParsing.ReadAntigravityQuotaSummary(summaryBody, DateTimeOffset.UtcNow, ShowAntigravityPartnerPool);
            return summary.Count > 0 ? live with { Gauges = summary } : live;
        }
        return null;
    }

    private async Task<string?> AskLanguageServerAsync(int port, string csrf, string method, string json, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        var request = PostJson($"http://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/{method}", json);
        request.Headers.TryAddWithoutValidation("x-codeium-csrf-token", csrf);
        request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");
        try
        {
            var (code, body) = await FetchAsync(request, deadline.Token);
            return (int)code is >= 200 and < 300 ? body : null;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException && !token.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool IsRunning(long pid)
    {
        if (pid is <= 0 or > int.MaxValue) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return !process.HasExited;
        }
        catch (Exception) { return false; }
    }
}
