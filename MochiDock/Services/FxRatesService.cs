using MochiDock.Core;

namespace MochiDock.Services;

public sealed class FxCache
{
    public int Version { get; set; }
    public Dictionary<string, MarketQuote> Markets { get; set; } = new();
    public FxQuote? Latest { get; set; }
    public DateTimeOffset RetryUntil { get; set; }
    public List<FxQuote> Hourly { get; set; } = new();
    public List<FxQuote> Daily { get; set; } = new();
    public List<DateTimeOffset> Requests { get; set; } = new();
    public DateTimeOffset NextCheck { get; set; }
    public DateTimeOffset LastManual { get; set; }
    public DateTimeOffset? LastChecked { get; set; }
    public DateTimeOffset? CollectionStarted { get; set; }
    public string HistoryError { get; set; } = "";
}

public sealed record FxViewState(FxQuote? Latest, FxQuote[] Hourly, FxQuote[] Daily, string Status,
    string HistoryError, DateTimeOffset? Checked, DateTimeOffset NextCheck, DateTimeOffset? CollectionStarted = null);

public sealed class FxRatesService : IDisposable
{
    readonly KeylessFxProvider? keyless;
    string[] pairs = FxPairPolicy.Normalize(null);
    /// <summary>The three pairs the card shows (always normalized, always three).</summary>
    public IReadOnlyList<string> Pairs => pairs;
    public string SelectedPair { get; private set; } = FxPairPolicy.Defaults[2];

    /// <summary>
    /// Replaces the shown pairs: keeps the selection on a selected pair, drops cached quotes and history of pairs
    /// that are no longer shown, and refreshes.
    /// </summary>
    public void SetPairs(IEnumerable<string>? requested)
    {
        var next = FxPairPolicy.Normalize(requested);
        if (next.SequenceEqual(pairs)) return;
        CancellationToken token;
        lock (configurationLock)
        {
            if (disposed) return;
            pairs = next;
            if (!next.Contains(SelectedPair)) SelectedPair = next[2];
            Invalidate();   // an in-flight refresh was for the old pairs
            token = configuration.Token;
        }
        _ = ApplyPairsAsync(next, token);
    }

    async Task ApplyPairsAsync(string[] next, CancellationToken token)
    {
        try
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (token.IsCancellationRequested) return;   // a newer change or shutdown took over
                foreach (var key in cache.Markets.Keys.Where(k => !next.Contains(k.Split(':')[0])).ToList()) cache.Markets.Remove(key);
                cache.NextCheck = default;
                Publish(State.Status);   // Changed handlers marshal to their own dispatcher
            }
            finally { gate.Release(); }
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }
    public string SelectedSpan { get; private set; } = "7D";
    public bool Hourly => SelectedSpan == "7D";
    bool visible;
    IReadOnlyDictionary<string, MarketQuote> publishedMarkets = new Dictionary<string, MarketQuote>();
    public IReadOnlyDictionary<string, MarketQuote> Markets => publishedMarkets;
    public void SelectMarket(string pair, bool hourly) { SelectedPair = pair; SelectedSpan = hourly ? "7D" : "1M"; _ = RefreshAsync(); }
    public void SelectSpan(string span) { SelectedSpan = span; _ = RefreshAsync(); }

    /// <summary>The selected pair's 24-hour change in percent (the figure the FX card's header shows), or null when unknown.</summary>
    public decimal? SelectedChangePercent() =>
        Markets.TryGetValue(SelectedPair, out var quote) ? quote.ChangePercent ?? Delta24h(quote.Value, SelectedPair, Markets) : null;

    /// <summary>A pair's 24-hour change from its reported change or its hourly history; null when neither is available.</summary>
    public static decimal? Delta24h(decimal currentValue, string pairName, IReadOnlyDictionary<string, MarketQuote> markets)
    {
        if (!markets.TryGetValue(pairName + ":1h", out var history) && !markets.TryGetValue(pairName, out history)) return null;
        if (history.ChangePercent.HasValue) return history.ChangePercent.Value;
        var bars = history.Bars;
        if (bars == null || bars.Length < 2) return null;
        var now = history.SourceAt > DateTimeOffset.MinValue ? history.SourceAt : DateTimeOffset.UtcNow;
        return FxMath.Calculate24hDelta(currentValue, bars.Select(b => (b.At, b.Value)), now);
    }
    public void SetVisible(bool value) { visible = value; if (value) { cache.NextCheck = default; _ = RefreshAsync(); } }
    public FxRatesService(KeylessFxProvider provider, string? directory = null, Func<DateTimeOffset>? clock = null, IEnumerable<string>? initialPairs = null)
        : this(null!, null!, directory, clock)
    {
        keyless = provider;
        pairs = FxPairPolicy.Normalize(initialPairs);
        SelectedPair = pairs[2];
    }
    readonly IFxRateProvider provider;
    readonly IFxCredentialStore credentials;
    readonly string path;
    readonly Func<DateTimeOffset> clock;
    readonly TimeSpan requestTimeout;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly SemaphoreSlim keyGate = new(1, 1);
    readonly object configurationLock = new();
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource configuration = new();
    readonly List<CancellationTokenSource> retired = new();
    readonly System.Threading.Timer timer;
    FxCache cache = new();
    bool enabled = true, suspended, disposed, authBlocked, historyBlocked, keyChanging;
    int generation;
    Task? stopTask;

    public FxViewState State { get; private set; } = new(null, [], [], "Loading", "", null, default);
    public event Action? Changed;

    public FxRatesService(IFxRateProvider provider, IFxCredentialStore credentials, string? directory = null,
        Func<DateTimeOffset>? clock = null, TimeSpan? requestTimeout = null)
    {
        this.provider = provider;
        this.credentials = credentials;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(8);
        path = System.IO.Path.Combine(directory ?? System.IO.Path.Combine(AppDataPaths.Local, "FxRates"), "rates.json");
        timer = new(_ => { _ = RefreshAsync(); }, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task StartAsync(bool isEnabled, bool runTimer = true)
    {
        await gate.WaitAsync();
        try
        {
            if (disposed) return;
            enabled = isEnabled;
            var loaded = await Task.Run(() => AtomicJsonStore.Load(path, () => new FxCache()), lifetime.Token).ConfigureAwait(false);
            cache = loaded.Value;
            cache.Markets ??= new();
            cache.Markets = cache.Markets.Where(p => p.Value is { Value: > 0 } q && q.SourceAt > DateTimeOffset.UnixEpoch && q.SourceAt <= clock().AddMinutes(5) && q.Bars is not null && (keyless is null || pairs.Contains(p.Key.Split(':')[0]))).ToDictionary(p => p.Key, p => p.Value);
            if (keyless is not null && cache.Version < 2)
            {
                if (System.IO.File.Exists(path) && !System.IO.File.Exists(path + ".v1.bak")) System.IO.File.Copy(path, path + ".v1.bak");
                cache.Version = 2;
            }
            ValidateCache();
            Publish(!enabled ? "Disabled — cached data retained." : loaded.CanWrite ? "Cached — checking configuration…" : "Failed to read cache; writes are blocked.");
            if (runTimer && !disposed) timer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }
        catch (OperationCanceledException) { }
        catch { Publish("Failed to read cached FX data."); }
        finally { gate.Release(); }
        if (runTimer) await RefreshAsync().ConfigureAwait(false);
    }

    void ValidateCache()
    {
        var now = clock();
        cache.Hourly ??= new(); cache.Daily ??= new(); cache.Requests ??= new();
        cache.Hourly.RemoveAll(q => q is null || !q.IsValidFor(null, now));
        cache.Daily.RemoveAll(q => q is null || !q.HistoricalDate.HasValue || !q.IsValidFor(q.HistoricalDate, now));
        if (cache.Latest is not null && !cache.Latest.IsValidFor(null, now)) cache.Latest = null;
        cache.CollectionStarted ??= cache.Hourly.Count > 0 ? cache.Hourly.Min(q => q.FetchedAt) : null;
        Prune();
    }

    void Prune()
    {
        var now = clock();
        var cutoff = now.AddDays(-8);
        // Keep one boundary observation so comparisons are possible at the retention edge.
        var ordered = cache.Hourly.OrderBy(q => q.SourceAt).DistinctBy(q => q.SourceAt).ToArray();
        var boundary = ordered.LastOrDefault(q => q.SourceAt < cutoff);
        cache.Hourly = ordered.Where(q => q.SourceAt >= cutoff).ToList();
        if (boundary is not null) cache.Hourly.Insert(0, boundary);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        cache.Daily = cache.Daily.Where(q => q.HistoricalDate >= today.AddDays(-8) && q.HistoricalDate < today)
            .DistinctBy(q => q.HistoricalDate).OrderBy(q => q.HistoricalDate).ToList();
        cache.Requests.RemoveAll(t => t <= now.AddDays(-31));
    }

    void Publish(string status)
    {
        publishedMarkets = new Dictionary<string, MarketQuote>(cache.Markets);
        State = new(cache.Latest ?? cache.Hourly.LastOrDefault(), cache.Hourly.ToArray(), cache.Daily.ToArray(),
            status, cache.HistoryError, cache.LastChecked, cache.NextCheck, cache.CollectionStarted);
        Changed?.Invoke();
    }

    Task Save(CancellationToken token) => Task.Run(() => AtomicJsonStore.Save(path, cache), token);
    void Invalidate()
    {
        generation++;
        configuration.Cancel();
        retired.Add(configuration);
        configuration = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
    }

    public void Configure(bool isEnabled, bool isSuspended)
    {
        CancellationToken token;
        lock (configurationLock)
        {
            if (disposed || enabled == isEnabled && suspended == isSuspended) return;
            enabled = isEnabled; suspended = isSuspended;
            Invalidate(); token = configuration.Token;
        }
        _ = ApplyConfigurationAsync(token);
    }

    async Task ApplyConfigurationAsync(CancellationToken token)
    {
        try
        {
            // A cancelled old request must finish before a resumed configuration refreshes.
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (token.IsCancellationRequested) return;
                if (!enabled) Publish("Disabled — cached data retained.");
                else if (suspended) Publish("Paused — cached data retained.");
            }
            finally { gate.Release(); }
            if (enabled && !suspended) await RefreshAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    public Task SaveKeyAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 512) throw new ArgumentException("Enter an App ID.");
        return ChangeKeyAsync(key.Trim());
    }
    public Task RemoveKeyAsync() => ChangeKeyAsync(null);

    async Task ChangeKeyAsync(string? key)
    {
        await keyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (configurationLock)
            {
                if (disposed) return;
                keyChanging = true; Invalidate();
            }
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (disposed) return;
                await Task.Run(() => { if (key is null) credentials.Remove(); else credentials.Save(key); }).ConfigureAwait(false);
                authBlocked = false; historyBlocked = false;
                cache.NextCheck = default; cache.RetryUntil = default;
                cache.HistoryError = "";
                // The manual throttle and request ledger survive key changes.
                await Save(lifetime.Token).ConfigureAwait(false);
                Publish(key is null ? "Missing key — showing cached data." : "App ID saved.");
            }
            finally { keyChanging = false; gate.Release(); }
            if (key is not null) await RefreshAsync().ConfigureAwait(false);
        }
        finally { keyGate.Release(); }
    }

    public async Task RefreshAsync(bool manual = false)
    {
        if (keyless is not null) { await RefreshKeyless(manual); return; }
        CancellationToken token;
        int version;
        lock (configurationLock)
        {
            if (disposed || keyChanging || suspended || !enabled) return;
            version = generation; token = configuration.Token;
        }
        if (!await gate.WaitAsync(0).ConfigureAwait(false)) return;
        bool Current() => !disposed && !token.IsCancellationRequested && version == generation;
        try
        {
            if (!Current()) return;
            var key = await Task.Run(credentials.Read, token).ConfigureAwait(false);
            if (!Current()) return;
            if (string.IsNullOrWhiteSpace(key)) { Publish("Missing key — enter an App ID in Settings."); return; }
            if (authBlocked) { Publish("Invalid App ID — update it in Settings."); return; }
            var now = clock();
            if (now < cache.RetryUntil) { Publish("Provider backoff or local quota active; cached data retained."); return; }
            if (manual)
            {
                if (now - cache.LastManual < TimeSpan.FromMinutes(5)) { Publish("Checked recently — manual refresh is available every five minutes."); return; }
                cache.LastManual = now;
            }
            else if (now < cache.NextCheck)
            {
                if (cache.Latest is not null && now - cache.Latest.SourceAt > TimeSpan.FromHours(2)) Publish("Cached/Stale");
                return;
            }
            cache.NextCheck = now.AddHours(1);
            Publish("Loading");

            async Task<FxQuote> Request(DateOnly? date)
            {
                token.ThrowIfCancellationRequested();
                Prune();
                if (!FxMath.WithinBudget(cache.Requests, clock()))
                {
                    var next = cache.Requests.Min().AddDays(31) - clock();
                    throw new FxProviderException("Local request budget reached (900 / rolling 31 days).", next);
                }
                cache.Requests.Add(clock());
                // Persist the attempted request before sending; failures consume quota too.
                await Save(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(requestTimeout);
                var operation = date.HasValue ? provider.HistoryAsync(key, date.Value, timeout.Token)
                    : provider.LatestAsync(key, timeout.Token);
                var quote = await operation.WaitAsync(timeout.Token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!quote.IsValidFor(date, clock())) throw new System.IO.InvalidDataException("Invalid FX quote or historical date.");
                return quote;
            }

            var latest = await Request(null).ConfigureAwait(false);
            if (!Current()) return;
            cache.LastChecked = clock();
            cache.CollectionStarted ??= clock();
            // A provider can return an older cached response: it must not roll the latest quote back.
            if (cache.Latest is null || latest.SourceAt >= cache.Latest.SourceAt) cache.Latest = latest;
            if (!cache.Hourly.Any(q => q.SourceAt == latest.SourceAt)) cache.Hourly.Add(latest);
            Prune(); await Save(token).ConfigureAwait(false);
            if (!Current()) return;
            Publish(clock() - cache.Latest!.SourceAt > TimeSpan.FromHours(2) ? "Cached/Stale" : "Ready");

            if (!historyBlocked)
            {
                var today = DateOnly.FromDateTime(clock().UtcDateTime);
                for (int day = 7; day >= 1; day--)
                {
                    var date = today.AddDays(-day);
                    if (cache.Daily.Any(q => q.HistoricalDate == date)) continue;
                    try
                    {
                        var quote = await Request(date).ConfigureAwait(false);
                        if (!Current()) return;
                        cache.Daily.Add(quote);
                        await Save(token).ConfigureAwait(false);
                    }
                    catch (FxProviderException e) when (e.HistoryDenied)
                    {
                        if (!Current()) return;
                        historyBlocked = true; cache.HistoryError = "Daily history is unavailable for this account.";
                        break;
                    }
                }
            }
            if (!Current()) return;
            Prune(); await Save(token).ConfigureAwait(false);
            if (Current()) Publish(clock() - cache.Latest!.SourceAt > TimeSpan.FromHours(2) ? "Cached/Stale" : "Ready");
        }
        catch (OperationCanceledException)
        {
            if (Current()) Publish("Failed — FX request timed out; cached data retained.");
        }
        catch (FxProviderException e)
        {
            if (!Current()) return;
            authBlocked = e.CredentialError;
            cache.RetryUntil = clock() + e.Retry; cache.NextCheck = cache.RetryUntil;
            Publish(e.Message);
            try { await Save(lifetime.Token).ConfigureAwait(false); } catch { /* The visible failure remains actionable. */ }
        }
        catch
        {
            if (Current()) Publish("Failed — FX data or cache unavailable; cached data retained.");
        }
        finally { gate.Release(); }
    }

    async Task RefreshKeyless(bool manual)
    {
        CancellationToken token; int version;
        lock (configurationLock) { if (disposed || suspended || !enabled) return; token = configuration.Token; version = generation; }
        if (!await gate.WaitAsync(0)) return;
        bool Current() => !disposed && !token.IsCancellationRequested && version == generation;
        try
        {
            var now = clock();
            if (manual && now - cache.LastManual < TimeSpan.FromMinutes(1)) { Publish("Checked recently — refresh available every minute."); return; }
            if (manual) cache.LastManual = now;
            var span = SelectedSpan;
            var chartKey = SelectedPair + ":" + span;
            var ttl = span switch { "1Y" => TimeSpan.FromHours(6), "1M" => TimeSpan.FromMinutes(30), _ => TimeSpan.FromMinutes(15) };
            var chartDue = !cache.Markets.TryGetValue(chartKey, out var chart) || now - chart.FetchedAt >= ttl;
            bool latestDue = manual || now >= cache.NextCheck;
            if (!latestDue && !chartDue) return;
            bool failed = false;
            if (latestDue)
            {
                cache.NextCheck = now + (visible ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(30));
                var shown = pairs;
                foreach (var pair in shown)
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(requestTimeout);
                        var quote = await keyless!.Yahoo(pair, span, timeout.Token);
                        if (!Current()) return;
                        if (!cache.Markets.TryGetValue(pair, out var prior) || quote.SourceAt >= prior.SourceAt) cache.Markets[pair] = quote;
                        cache.Markets[pair + ":" + span] = quote;
                        if (span is "7D" or "1M") cache.Markets[pair + ":1h"] = quote;   // both are hourly: the 24-hour change reads this
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch { failed = true; }
                }
                if (failed)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(24));
                    try { foreach (var quote in await keyless!.Fallback(shown, timeout.Token)) { if (!Current()) return; if (!cache.Markets.TryGetValue(quote.Pair, out var old) || (old.Provider == "Yahoo Finance" ? now - old.SourceAt > TimeSpan.FromHours(2) : quote.SourceAt >= old.SourceAt)) cache.Markets[quote.Pair] = quote; } }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch { }
                }
                cache.LastChecked = now;
            }
            if (chartDue || manual)
            {
                var pair = SelectedPair;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(requestTimeout);
                    var quote = await keyless!.Yahoo(pair, span, timeout.Token);
                    if (!Current()) return;
                    cache.Markets[pair + ":" + span] = quote;
                    if (span is "7D" or "1M") cache.Markets[pair + ":1h"] = quote;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { failed = true; }
            }
            if (!Current()) return;
            await Save(token);
            if (Current()) Publish(failed ? "Some sources unavailable — cached data retained." : "Ready");
        }
        catch (OperationCanceledException) { }
        catch { if (Current()) Publish("FX unavailable — cached data retained."); }
        finally { gate.Release(); }
    }

    public Task StopAsync()
    {
        lock (configurationLock)
        {
            if (stopTask is not null) return stopTask;
            disposed = true; generation++;
            timer.Dispose(); configuration.Cancel(); lifetime.Cancel();
            return stopTask = DrainAsync();
        }
    }

    async Task DrainAsync()
    {
        await keyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                configuration.Dispose();
                foreach (var c in retired) c.Dispose();
                retired.Clear();
            }
            finally { gate.Release(); }
        }
        finally { keyGate.Release(); }
    }
    public void Dispose() { _ = StopAsync(); }
}
