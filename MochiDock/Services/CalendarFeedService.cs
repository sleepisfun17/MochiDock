using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MochiDock.Core;

namespace MochiDock.Services;

public enum CalendarFeedState { NotConnected, Loading, Updated, Offline, Invalid }

/// <summary>Seals the feed link at rest. The app uses Windows DPAPI (current user); tests use a stand-in.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] sealedBytes);
}

public sealed class CalendarCache
{
    public string UrlHash { get; set; } = "";
    public DateTime FetchedUtc { get; set; }
    public string? ETag { get; set; }
    public List<AgendaEvent> Events { get; set; } = new();
}

/// <summary>
/// Read-only calendar feed (Google Calendar's "secret address in iCal format", or any https/webcal .ics link) shown on the Today card.
/// The link is a secret: it is DPAPI-protected in gcal.dat (never in settings.json, exports or logs) and only ever sent to its own host.
/// Refreshes run only while the Today card is visible (immediately when shown if stale, then every 5 minutes); the last good agenda is cached
/// atomically so it still shows offline. Stale results from a replaced or disconnected feed are dropped by a generation check.
/// </summary>
public sealed class CalendarFeedService : IDisposable
{
    public const int MaxFeedBytes = 8 * 1024 * 1024;
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(5);
    /// <summary>Refresh interval while the Today card is closed but something (reminders) still needs fresh events.</summary>
    public static readonly TimeSpan BackgroundRefreshEvery = TimeSpan.FromMinutes(15);
    static readonly TimeSpan MinGap = TimeSpan.FromSeconds(60);
    const int DaysBefore = 35, DaysAfter = 70;

    readonly HttpClient _http;
    readonly ISecretProtector _protector;
    readonly string _folder, _secretPath, _cachePath;
    readonly Func<DateTime> _now;
    readonly object _gate = new();
    System.Threading.Timer? _timer;
    CancellationTokenSource _cts = new();
    long _generation;
    string? _url, _etag;
    bool _visible, _background, _refreshing, _disposed;
    DateTime _lastAttempt = DateTime.MinValue;

    public event Action? Changed;

    public IReadOnlyList<AgendaEvent> Events { get; private set; } = Array.Empty<AgendaEvent>();
    public CalendarFeedState State { get; private set; } = CalendarFeedState.NotConnected;
    public DateTime? UpdatedAt { get; private set; }
    public string Message { get; private set; } = "";
    public bool IsConnected => _url is not null;
    public bool IsRefreshing { get { lock (_gate) return _refreshing; } }

    public string StatusText => State switch
    {
        CalendarFeedState.NotConnected => "Calendar not connected",
        CalendarFeedState.Loading => "Loading calendar…",
        CalendarFeedState.Updated => $"Calendar updated {UpdatedAt:HH:mm}",
        CalendarFeedState.Offline => UpdatedAt is { } at ? $"Offline · showing {at:HH:mm}" : "Calendar offline",
        _ => string.IsNullOrEmpty(Message) ? "Calendar link not accepted" : Message
    };

    public CalendarFeedService(ISecretProtector protector, HttpClient? http = null, string? folder = null, Func<DateTime>? now = null)
    {
        _protector = protector;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        if (http is null) _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppUserAgent.Value);
        _folder = folder ?? AppDataPaths.Local;
        _secretPath = Path.Combine(_folder, "gcal.dat");
        _cachePath = Path.Combine(_folder, "Calendar", "events.json");
        _now = now ?? (() => DateTime.Now);
        LoadSecret();
        LoadCache();
    }

    // ---- connect / disconnect ---------------------------------------------------------------------------------------

    /// <summary>Validates and stores the feed link, then starts loading it. Returns an error message for the user, or "" on success.</summary>
    public string Connect(string text)
    {
        var uri = AgendaLogic.NormalizeFeedUrl(text);
        if (uri is null) return "Paste the calendar's https:// (or webcal://) link — Google Calendar: Settings › your calendar › Secret address in iCal format.";
        try
        {
            Directory.CreateDirectory(_folder);
            var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Secret { Url = uri.ToString() })));
            DpapiProtector.WriteAtomicBytes(_secretPath, protectedBytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return "Couldn't save the link securely: " + e.Message;
        }
        lock (_gate)
        {
            _url = uri.ToString();
            _etag = null;
            _generation++;
            Events = Array.Empty<AgendaEvent>();
            UpdatedAt = null;
            State = CalendarFeedState.Loading;
            Message = "";
            RestartTimer();   // connected while the card is already visible
        }
        DeleteCache();
        RaiseChanged();
        _ = RefreshAsync(force: true);
        return "";
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            _url = null; _etag = null; _generation++;
            RestartTimer();
            _cts.Cancel(); _cts.Dispose(); _cts = new CancellationTokenSource();
            Events = Array.Empty<AgendaEvent>();
            UpdatedAt = null; Message = "";
            State = CalendarFeedState.NotConnected;
        }
        try { if (File.Exists(_secretPath)) File.Delete(_secretPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        DeleteCache();
        RaiseChanged();
    }

    // ---- refresh ----------------------------------------------------------------------------------------------------

    /// <summary>Called by the Today card: the feed is only polled while it is on screen.</summary>
    public void SetVisible(bool visible)
    {
        lock (_gate)
        {
            if (_disposed || _visible == visible) return;
            _visible = visible;
            RestartTimer();
        }
        // Showing the card refreshes at once unless the last attempt was under a minute ago.
        if (visible) _ = RefreshAsync(force: false);
    }

    /// <summary>
    /// Keeps the feed fresh (every 15 minutes) while the Today card is closed. Turn it on only while something needs
    /// up-to-date events, such as reminders; the 5-minute refresh while the card is visible is unchanged.
    /// </summary>
    public void SetBackground(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed || _background == enabled) return;
            _background = enabled;
            RestartTimer();
        }
        if (enabled) _ = RefreshAsync(force: false);
    }

    /// <summary>True while the feed refreshes on a timer even though the Today card is closed.</summary>
    public bool IsBackgroundRefreshing { get { lock (_gate) return _background && _url is not null && !_disposed; } }

    // Caller holds _gate. The timer exists only while a feed is connected and either the card is visible or background refresh is on.
    void RestartTimer()
    {
        _timer?.Dispose(); _timer = null;
        if ((_visible || _background) && !_disposed && _url is not null)
        {
            var every = _visible ? RefreshEvery : BackgroundRefreshEvery;
            _timer = new System.Threading.Timer(_ => _ = RefreshAsync(force: false), null, every, every);
        }
    }

    public async Task RefreshAsync(bool force)
    {
        string url; long generation; string? etag; CancellationToken token;
        lock (_gate)
        {
            if (_disposed || _url is null || _refreshing) return;
            if (!force && _now() - _lastAttempt < MinGap) return;
            _refreshing = true; _lastAttempt = _now();
            url = _url; generation = _generation; etag = _etag; token = _cts.Token;
            if (Events.Count == 0) State = CalendarFeedState.Loading;
        }
        RaiseChanged();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("text/calendar");
            if (!string.IsNullOrEmpty(etag) && !force) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                Apply(generation, null, null, CalendarFeedState.Updated, "");
            }
            else if (response.IsSuccessStatusCode)
            {
                if (response.Content.Headers.ContentLength is > MaxFeedBytes)
                { Apply(generation, null, null, CalendarFeedState.Invalid, "Calendar feed is too large to read."); return; }
                string body = await ReadCappedAsync(response.Content, token);
                if (body.Length == 0 && response.Content.Headers.ContentLength is > 0)
                { Apply(generation, null, null, CalendarFeedState.Invalid, "Calendar feed is too large to read."); return; }
                if (!IcsParser.LooksLikeCalendar(body))
                { Apply(generation, null, null, CalendarFeedState.Invalid, "That link isn't a calendar feed (.ics)."); return; }

                var today = _now().Date;
                var events = await Task.Run(() => IcsParser.Read(body, today.AddDays(-DaysBefore), today.AddDays(DaysAfter)), token);
                Apply(generation, events, response.Headers.ETag?.ToString(), CalendarFeedState.Updated, "");
            }
            else if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                Apply(generation, null, null, CalendarFeedState.Invalid, $"Calendar link not accepted (HTTP {(int)response.StatusCode}). Check the secret address.");
            }
            else
            {
                Apply(generation, null, null, CalendarFeedState.Offline, $"HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            if (!token.IsCancellationRequested) Apply(generation, null, null, CalendarFeedState.Offline, "");
        }
        finally
        {
            lock (_gate) _refreshing = false;
        }
    }

    static async Task<string> ReadCappedAsync(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920]; int read;
        while ((read = await stream.ReadAsync(chunk, token)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxFeedBytes) return "";
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    // Applies a result only if the feed has not been replaced or disconnected since the request started.
    void Apply(long generation, IReadOnlyList<AgendaEvent>? events, string? etag, CalendarFeedState state, string message)
    {
        CalendarCache? toSave = null;
        lock (_gate)
        {
            if (generation != _generation || _url is null) return;
            if (events is not null) { Events = events; _etag = etag; UpdatedAt = _now(); }
            else if (state == CalendarFeedState.Updated) UpdatedAt = _now();   // 304: unchanged
            State = state; Message = message;
            if (state == CalendarFeedState.Updated) toSave = new CalendarCache { UrlHash = Hash(_url), FetchedUtc = DateTime.UtcNow, ETag = _etag, Events = Events.ToList() };
        }
        if (toSave is not null) SaveCache(toSave);
        RaiseChanged();
    }

    // ---- storage ----------------------------------------------------------------------------------------------------

    sealed class Secret { public string Url { get; set; } = ""; }

    void LoadSecret()
    {
        try
        {
            if (!File.Exists(_secretPath)) return;
            var json = Encoding.UTF8.GetString(_protector.Unprotect(File.ReadAllBytes(_secretPath)));
            var secret = JsonSerializer.Deserialize<Secret>(json);
            if (AgendaLogic.NormalizeFeedUrl(secret?.Url) is { } uri) { _url = uri.ToString(); State = CalendarFeedState.Loading; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { }
    }

    void LoadCache()
    {
        if (_url is null) return;
        try
        {
            var loaded = AtomicJsonStore.Load(_cachePath, () => new CalendarCache());
            var cache = loaded.Value;
            if (loaded.Status is JsonLoadStatus.Loaded or JsonLoadStatus.Recovered && cache.UrlHash == Hash(_url))
            {
                Events = cache.Events; _etag = cache.ETag; UpdatedAt = cache.FetchedUtc.ToLocalTime(); State = CalendarFeedState.Updated;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }

    void SaveCache(CalendarCache cache)
    {
        try { AtomicJsonStore.Save(_cachePath, cache); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    void DeleteCache()
    {
        try { if (File.Exists(_cachePath)) File.Delete(_cachePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static string Hash(string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));

    void RaiseChanged() { try { Changed?.Invoke(); } catch (Exception e) { System.Diagnostics.Debug.WriteLine("Calendar changed handler: " + e.Message); } }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose(); _timer = null;
            _cts.Cancel();
        }
    }
}
