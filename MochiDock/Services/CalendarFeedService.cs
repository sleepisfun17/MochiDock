using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MochiDock.Core;

namespace MochiDock.Services;

public enum CalendarFeedState { NotConnected, Loading, Updated, Offline, Invalid }

/// <summary>Seals the feed links at rest. The app uses Windows DPAPI (current user); tests use a stand-in.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] sealedBytes);
}

public sealed record CalendarFeedInfo(
    string Id,
    string Name,
    string Color,
    CalendarFeedState State,
    DateTime? UpdatedAt,
    string Message,
    int EventCount);

public sealed class CalendarFeedCacheEntry
{
    public string UrlHash { get; set; } = "";
    public DateTime FetchedUtc { get; set; }
    public string? ETag { get; set; }
    public List<AgendaEvent> Events { get; set; } = new();
}

public sealed class CalendarCache
{
    public string UrlHash { get; set; } = "";
    public DateTime FetchedUtc { get; set; }
    public string? ETag { get; set; }
    public List<AgendaEvent> Events { get; set; } = new();
    public List<CalendarFeedCacheEntry>? Feeds { get; set; }
}

/// <summary>
/// Read-only calendar feeds (Google Calendar's "secret address in iCal format", or any https/webcal .ics link) shown on the Today card.
/// Links are secrets: DPAPI-protected together in gcal.dat (never in settings.json, exports or logs) and only sent to their own hosts.
/// Refreshes run every 5 minutes while the Today card is visible (or every 15 minutes when background refresh is on), with atomic per-feed caching.
/// </summary>
public sealed class CalendarFeedService : IDisposable
{
    public const int MaxFeedBytes = 8 * 1024 * 1024;
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(5);
    /// <summary>Refresh interval while the Today card is closed but reminders or Mochi's meeting face still need fresh events.</summary>
    public static readonly TimeSpan BackgroundRefreshEvery = TimeSpan.FromMinutes(15);
    static readonly TimeSpan MinGap = TimeSpan.FromSeconds(60);
    const int DaysBefore = 35, DaysAfter = 70;

    sealed class FeedSlot
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "Calendar";
        public string Color { get; set; } = CalendarFeedPolicy.DefaultColor;
        public string Url { get; set; } = "";
        public string? ETag { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public CalendarFeedState State { get; set; } = CalendarFeedState.Loading;
        public string Message { get; set; } = "";
        public IReadOnlyList<AgendaEvent> Events { get; set; } = Array.Empty<AgendaEvent>();
        public bool Refreshing { get; set; }
        public DateTime LastAttempt { get; set; } = DateTime.MinValue;
    }

    sealed class FeedSecret
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Color { get; set; } = "";
        public string Url { get; set; } = "";
    }

    sealed class SecretStore
    {
        public string Url { get; set; } = "";
        public List<FeedSecret>? Feeds { get; set; }
    }

    readonly HttpClient _http;
    readonly ISecretProtector _protector;
    readonly string _folder, _secretPath, _cachePath;
    readonly Func<DateTime> _now;
    readonly object _gate = new();
    readonly List<FeedSlot> _slots = new();
    System.Threading.Timer? _timer;
    CancellationTokenSource _cts = new();
    long _generation;
    bool _visible, _background, _disposed;

    public event Action? Changed;

    public IReadOnlyList<AgendaEvent> Events { get; private set; } = Array.Empty<AgendaEvent>();
    public IReadOnlyList<CalendarFeedInfo> Feeds { get; private set; } = Array.Empty<CalendarFeedInfo>();
    public CalendarFeedState State { get; private set; } = CalendarFeedState.NotConnected;
    public DateTime? UpdatedAt { get; private set; }
    public string Message { get; private set; } = "";
    public bool IsConnected => Feeds.Count > 0;
    public bool IsRefreshing { get { lock (_gate) return _slots.Any(s => s.Refreshing); } }

    public string StatusText
    {
        get
        {
            var feeds = Feeds;
            if (feeds.Count == 0) return "Calendar not connected";
            if (feeds.Count == 1)
            {
                return State switch
                {
                    CalendarFeedState.NotConnected => "Calendar not connected",
                    CalendarFeedState.Loading => "Loading calendar…",
                    CalendarFeedState.Updated => $"Calendar updated {UpdatedAt:HH:mm}",
                    CalendarFeedState.Offline => UpdatedAt is { } at ? $"Offline · showing {at:HH:mm}" : "Calendar offline",
                    _ => string.IsNullOrEmpty(Message) ? "Calendar link not accepted" : Message
                };
            }

            int invalidCount = feeds.Count(f => f.State == CalendarFeedState.Invalid);
            int offlineCount = feeds.Count(f => f.State == CalendarFeedState.Offline);
            int updatedCount = feeds.Count(f => f.State == CalendarFeedState.Updated);

            if (feeds.All(f => f.State == CalendarFeedState.Loading)) return "Loading calendars…";
            if (invalidCount > 0)
                return invalidCount == feeds.Count
                    ? $"{feeds.Count} calendars failed · check Settings"
                    : $"{invalidCount} of {feeds.Count} calendars failed · check Settings";
            if (updatedCount > 0)
                return offlineCount > 0
                    ? $"{offlineCount} of {feeds.Count} calendars offline · updated {UpdatedAt:HH:mm}"
                    : $"{feeds.Count} calendars updated {UpdatedAt:HH:mm}";
            return UpdatedAt is { } latest ? $"Offline · showing {latest:HH:mm}" : "Calendars offline";
        }
    }

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
        lock (_gate) RebuildSnapshotLocked();
    }

    // ---- connect / add / update / remove / disconnect ---------------------------------------------------------------

    /// <summary>Replaces all calendars with one default calendar link and starts loading it. Returns an error message or "".</summary>
    public string Connect(string text)
    {
        var uri = AgendaLogic.NormalizeFeedUrl(text);
        if (uri is null) return InvalidUrlMessage;

        var slot = new FeedSlot
        {
            Id = "default",
            Name = "Calendar",
            Color = CalendarFeedPolicy.DefaultColor,
            Url = uri.ToString(),
            State = CalendarFeedState.Loading,
        };
        if (SaveSecrets(new[] { slot }) is { } err) return err;

        lock (_gate)
        {
            _generation++;
            _cts.Cancel(); _cts.Dispose(); _cts = new CancellationTokenSource();
            _slots.Clear();
            _slots.Add(slot);
            RestartTimer();
            RebuildSnapshotLocked();
        }
        DeleteCache();
        RaiseChanged();
        _ = RefreshAsync(force: true);
        return "";
    }

    /// <summary>Validates and appends a named, colour-coded calendar feed, then starts loading it. Returns an error message or "".</summary>
    public string AddFeed(string? name, string url, string? color = null)
    {
        var uri = AgendaLogic.NormalizeFeedUrl(url);
        if (uri is null) return InvalidUrlMessage;
        string normalizedUrl = uri.ToString();

        FeedSlot slot;
        List<FeedSlot> nextSlots;
        lock (_gate)
        {
            if (_disposed) return "Calendar service is closed.";
            if (_slots.Count >= CalendarFeedPolicy.MaxFeeds)
                return $"You can connect up to {CalendarFeedPolicy.MaxFeeds} calendars.";
            if (_slots.Any(s => string.Equals(s.Url, normalizedUrl, StringComparison.Ordinal)))
                return "That calendar link is already connected.";

            slot = new FeedSlot
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = CalendarFeedPolicy.NormalizeName(name, _slots.Count + 1),
                Color = string.IsNullOrWhiteSpace(color)
                    ? CalendarFeedPolicy.PickNextColor(_slots.Select(s => s.Color))
                    : CalendarFeedPolicy.NormalizeColor(color),
                Url = normalizedUrl,
                State = CalendarFeedState.Loading,
            };
            nextSlots = _slots.Append(slot).ToList();
        }

        if (SaveSecrets(nextSlots) is { } err) return err;

        lock (_gate)
        {
            if (_disposed) return "Calendar service is closed.";
            if (_slots.Any(s => string.Equals(s.Url, normalizedUrl, StringComparison.Ordinal)))
                return "That calendar link is already connected.";
            _slots.Add(slot);
            RestartTimer();
            RebuildSnapshotLocked();
        }
        RaiseChanged();
        _ = RefreshSlotAsync(slot.Id, force: true);
        return "";
    }

    /// <summary>Updates a connected calendar's display name and colour preset and immediately re-tags its events.</summary>
    public string UpdateFeed(string id, string? name, string? color)
    {
        List<FeedSlot> snapshot;
        lock (_gate)
        {
            var slot = _slots.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
            if (slot is null) return "Calendar not found.";
            slot.Name = CalendarFeedPolicy.NormalizeName(name, _slots.IndexOf(slot) + 1);
            slot.Color = CalendarFeedPolicy.NormalizeColor(color);
            slot.Events = TagEvents(slot.Events, slot.Name, slot.Color);
            snapshot = _slots.ToList();
        }

        if (SaveSecrets(snapshot) is { } err) return err;

        CalendarCache cache;
        lock (_gate)
        {
            RebuildSnapshotLocked();
            cache = BuildCacheLocked();
        }
        SaveCache(cache);
        RaiseChanged();
        return "";
    }

    /// <summary>Disconnects one calendar feed and removes its cached events without affecting other linked calendars.</summary>
    public void RemoveFeed(string id)
    {
        List<FeedSlot> remaining;
        lock (_gate)
        {
            int idx = _slots.FindIndex(s => string.Equals(s.Id, id, StringComparison.Ordinal));
            if (idx < 0) return;
            _slots.RemoveAt(idx);
            RestartTimer();
            RebuildSnapshotLocked();
            remaining = _slots.ToList();
        }

        if (remaining.Count == 0)
        {
            DeleteSecretsFile();
            DeleteCache();
        }
        else
        {
            _ = SaveSecrets(remaining);
            CalendarCache cache;
            lock (_gate) cache = BuildCacheLocked();
            SaveCache(cache);
        }
        RaiseChanged();
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            _generation++;
            _cts.Cancel(); _cts.Dispose(); _cts = new CancellationTokenSource();
            _slots.Clear();
            RestartTimer();
            RebuildSnapshotLocked();
        }
        DeleteSecretsFile();
        DeleteCache();
        RaiseChanged();
    }

    const string InvalidUrlMessage =
        "Paste the calendar's https:// (or webcal://) link — Google Calendar: Settings › your calendar › Secret address in iCal format.";

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
        if (visible) _ = RefreshAsync(force: false);
    }

    /// <summary>
    /// Keeps all connected feeds fresh (every 15 minutes) while the Today card is closed. Turn it on only while something needs
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

    /// <summary>True while connected feeds refresh on a timer even though the Today card is closed.</summary>
    public bool IsBackgroundRefreshing { get { lock (_gate) return _background && _slots.Count > 0 && !_disposed; } }

    // Caller holds _gate. The timer exists only while at least one feed is connected and visible or background refresh is on.
    void RestartTimer()
    {
        _timer?.Dispose(); _timer = null;
        if ((_visible || _background) && !_disposed && _slots.Count > 0)
        {
            var every = _visible ? RefreshEvery : BackgroundRefreshEvery;
            _timer = new System.Threading.Timer(_ => _ = RefreshAsync(force: false), null, every, every);
        }
    }

    public Task RefreshAsync(bool force)
    {
        List<string> ids;
        lock (_gate)
        {
            if (_disposed || _slots.Count == 0) return Task.CompletedTask;
            ids = _slots.Select(s => s.Id).ToList();
        }
        return Task.WhenAll(ids.Select(id => RefreshSlotAsync(id, force)));
    }

    async Task RefreshSlotAsync(string id, bool force)
    {
        string url; long generation; string? etag; CancellationToken token;
        lock (_gate)
        {
            if (_disposed) return;
            var slot = _slots.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
            if (slot is null || slot.Refreshing) return;
            if (!force && _now() - slot.LastAttempt < MinGap) return;
            slot.Refreshing = true;
            slot.LastAttempt = _now();
            url = slot.Url;
            generation = _generation;
            etag = slot.ETag;
            token = _cts.Token;
            if (slot.Events.Count == 0) slot.State = CalendarFeedState.Loading;
            RebuildSnapshotLocked();
        }
        RaiseChanged();

        bool shouldRaise = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("text/calendar");
            if (!string.IsNullOrEmpty(etag) && !force) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                shouldRaise = ApplySlot(id, generation, url, null, null, CalendarFeedState.Updated, "");
            }
            else if (response.IsSuccessStatusCode)
            {
                if (response.Content.Headers.ContentLength is > MaxFeedBytes)
                { shouldRaise = ApplySlot(id, generation, url, null, null, CalendarFeedState.Invalid, "Calendar feed is too large to read."); return; }
                string body = await ReadCappedAsync(response.Content, token);
                if (body.Length == 0 && response.Content.Headers.ContentLength is > 0)
                { shouldRaise = ApplySlot(id, generation, url, null, null, CalendarFeedState.Invalid, "Calendar feed is too large to read."); return; }
                if (!IcsParser.LooksLikeCalendar(body))
                { shouldRaise = ApplySlot(id, generation, url, null, null, CalendarFeedState.Invalid, "That link isn't a calendar feed (.ics)."); return; }

                var today = _now().Date;
                var events = await Task.Run(() => IcsParser.Read(body, today.AddDays(-DaysBefore), today.AddDays(DaysAfter)), token);
                shouldRaise = ApplySlot(id, generation, url, events, response.Headers.ETag?.ToString(), CalendarFeedState.Updated, "");
            }
            else if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                shouldRaise = ApplySlot(id, generation, url, null, null, CalendarFeedState.Invalid, $"Calendar link not accepted (HTTP {(int)response.StatusCode}). Check the secret address.");
            }
            else
            {
                shouldRaise = ApplySlot(id, generation, url, null, null, CalendarFeedState.Offline, $"HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            if (!token.IsCancellationRequested) shouldRaise = ApplySlot(id, generation, url, null, null, CalendarFeedState.Offline, "");
        }
        finally
        {
            lock (_gate)
            {
                if (_slots.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal)) is { } slot)
                    slot.Refreshing = false;
            }
            if (shouldRaise) RaiseChanged();
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

    bool ApplySlot(string id, long generation, string url, IReadOnlyList<AgendaEvent>? events, string? etag, CalendarFeedState state, string message)
    {
        CalendarCache? toSave = null;
        lock (_gate)
        {
            if (generation != _generation) return false;
            var slot = _slots.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
            if (slot is null || !string.Equals(slot.Url, url, StringComparison.Ordinal)) return false;

            if (events is not null)
            {
                slot.Events = TagEvents(events, slot.Name, slot.Color);
                slot.ETag = etag;
                slot.UpdatedAt = _now();
            }
            else if (state == CalendarFeedState.Updated)
            {
                slot.UpdatedAt = _now(); // 304: unchanged
            }
            slot.State = state;
            slot.Message = message;
            RebuildSnapshotLocked();
            if (state == CalendarFeedState.Updated) toSave = BuildCacheLocked();
        }
        if (toSave is not null) SaveCache(toSave);
        return true;
    }

    static IReadOnlyList<AgendaEvent> TagEvents(IReadOnlyList<AgendaEvent> events, string name, string color) =>
        events.Select(e => e with { CalendarName = name, CalendarColor = color }).ToList();

    // Caller holds _gate.
    void RebuildSnapshotLocked()
    {
        Feeds = _slots.Select(s => new CalendarFeedInfo(
            s.Id, s.Name, s.Color, s.State, s.UpdatedAt, s.Message, s.Events.Count)).ToList();

        if (_slots.Count == 0)
        {
            Events = Array.Empty<AgendaEvent>();
            State = CalendarFeedState.NotConnected;
            UpdatedAt = null;
            Message = "";
            return;
        }

        Events = _slots.Count == 1
            ? _slots[0].Events
            : _slots.SelectMany(s => s.Events).OrderBy(e => e.Start).ThenBy(e => e.End).ToList();

        UpdatedAt = _slots.Max(s => s.UpdatedAt);

        if (_slots.Count == 1)
        {
            State = _slots[0].State;
            Message = _slots[0].Message;
            return;
        }

        int invalidCount = _slots.Count(s => s.State == CalendarFeedState.Invalid);
        int updatedCount = _slots.Count(s => s.State == CalendarFeedState.Updated);

        if (_slots.All(s => s.State == CalendarFeedState.Loading))
        {
            State = CalendarFeedState.Loading;
            Message = "";
        }
        else if (invalidCount > 0)
        {
            State = CalendarFeedState.Invalid;
            Message = invalidCount == _slots.Count
                ? $"{_slots.Count} calendars failed · check Settings"
                : $"{invalidCount} of {_slots.Count} calendars failed · check Settings";
        }
        else if (updatedCount > 0)
        {
            State = CalendarFeedState.Updated;
            Message = "";
        }
        else
        {
            State = CalendarFeedState.Offline;
            Message = "";
        }
    }

    // ---- storage ----------------------------------------------------------------------------------------------------

    string? SaveSecrets(IEnumerable<FeedSlot> slots)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            var store = new SecretStore
            {
                Feeds = slots.Select(s => new FeedSecret
                {
                    Id = s.Id,
                    Name = s.Name,
                    Color = s.Color,
                    Url = s.Url,
                }).ToList(),
            };
            var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(store)));
            DpapiProtector.WriteAtomicBytes(_secretPath, protectedBytes);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return "Couldn't save the link securely: " + e.Message;
        }
    }

    void DeleteSecretsFile()
    {
        try { if (File.Exists(_secretPath)) File.Delete(_secretPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    void LoadSecret()
    {
        try
        {
            if (!File.Exists(_secretPath)) return;
            var json = Encoding.UTF8.GetString(_protector.Unprotect(File.ReadAllBytes(_secretPath)));
            var store = JsonSerializer.Deserialize<SecretStore>(json);
            if (store is null) return;

            if (store.Feeds is { Count: > 0 })
            {
                int index = 0;
                foreach (var f in store.Feeds.Take(CalendarFeedPolicy.MaxFeeds))
                {
                    if (AgendaLogic.NormalizeFeedUrl(f.Url) is not { } uri) continue;
                    index++;
                    _slots.Add(new FeedSlot
                    {
                        Id = string.IsNullOrWhiteSpace(f.Id) ? Guid.NewGuid().ToString("N") : f.Id,
                        Name = CalendarFeedPolicy.NormalizeName(f.Name, index),
                        Color = string.IsNullOrWhiteSpace(f.Color)
                            ? CalendarFeedPolicy.PickNextColor(_slots.Select(s => s.Color))
                            : CalendarFeedPolicy.NormalizeColor(f.Color),
                        Url = uri.ToString(),
                        State = CalendarFeedState.Loading,
                    });
                }
            }
            else if (AgendaLogic.NormalizeFeedUrl(store.Url) is { } legacyUri)
            {
                _slots.Add(new FeedSlot
                {
                    Id = "default",
                    Name = "Calendar",
                    Color = CalendarFeedPolicy.DefaultColor,
                    Url = legacyUri.ToString(),
                    State = CalendarFeedState.Loading,
                });
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { }
    }

    void LoadCache()
    {
        if (_slots.Count == 0) return;
        try
        {
            var loaded = AtomicJsonStore.Load(_cachePath, () => new CalendarCache());
            if (loaded.Status is not (JsonLoadStatus.Loaded or JsonLoadStatus.Recovered)) return;
            var cache = loaded.Value;

            if (cache.Feeds is { Count: > 0 })
            {
                foreach (var slot in _slots)
                {
                    string hash = Hash(slot.Url);
                    var entry = cache.Feeds.FirstOrDefault(e => e.UrlHash == hash);
                    if (entry is null) continue;
                    slot.Events = TagEvents(entry.Events, slot.Name, slot.Color);
                    slot.ETag = entry.ETag;
                    slot.UpdatedAt = entry.FetchedUtc.ToLocalTime();
                    slot.State = CalendarFeedState.Updated;
                }
            }
            else if (_slots.Count == 1 && cache.UrlHash == Hash(_slots[0].Url))
            {
                var slot = _slots[0];
                slot.Events = TagEvents(cache.Events, slot.Name, slot.Color);
                slot.ETag = cache.ETag;
                slot.UpdatedAt = cache.FetchedUtc.ToLocalTime();
                slot.State = CalendarFeedState.Updated;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }

    // Caller holds _gate.
    CalendarCache BuildCacheLocked() => new()
    {
        Feeds = _slots.Where(s => s.UpdatedAt.HasValue || s.Events.Count > 0 || !string.IsNullOrEmpty(s.ETag))
            .Select(s => new CalendarFeedCacheEntry
            {
                UrlHash = Hash(s.Url),
                FetchedUtc = s.UpdatedAt?.ToUniversalTime() ?? DateTime.UtcNow,
                ETag = s.ETag,
                Events = s.Events.ToList(),
            }).ToList(),
    };

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
