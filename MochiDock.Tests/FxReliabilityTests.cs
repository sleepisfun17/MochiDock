using MochiDock.Core;
using MochiDock.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

public sealed class FxReliabilityTests
{
    sealed class Keys : IFxCredentialStore
    {
        public string? Key = "fake";
        public string? Read() => Key;
        public void Save(string key) => Key = key;
        public void Remove() => Key = null;
    }
    sealed class Provider : IFxRateProvider
    {
        public int LatestCalls, HistoryCalls;
        public required Func<CancellationToken, Task<FxQuote>> Latest;
        public required Func<DateOnly, CancellationToken, Task<FxQuote>> History;
        public Task<FxQuote> LatestAsync(string key, CancellationToken token) { LatestCalls++; return Latest(token); }
        public Task<FxQuote> HistoryAsync(string key, DateOnly date, CancellationToken token) { HistoryCalls++; return History(date, token); }
    }
    sealed class Fixture : IAsyncDisposable
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-11-06T12:00:00Z");
        public string DirectoryPath = Path.Combine(Path.GetTempPath(), "MochiFx-" + Guid.NewGuid().ToString("N"));
        public Keys Keys = new();
        public Provider Provider;
        public FxRatesService Service;
        public string FilePath => Path.Combine(DirectoryPath, "rates.json");
        public Fixture(TimeSpan? timeout = null)
        {
            Provider = new() { Latest = _ => Task.FromResult(Quote()), History = (d, _) => Task.FromResult(Quote(d)) };
            Service = new(Provider, Keys, DirectoryPath, () => Now, timeout);
        }
        public FxQuote Quote(DateOnly? date = null) => new(date.HasValue ? new DateTimeOffset(date.Value.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero) : Now, Now, 150.123456m, 15000.123456m, date);
        public async ValueTask DisposeAsync() { await Service.StopAsync(); if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
    }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }

    [Fact]
    public void DailyUtcBoundariesAndHourlyRangesStaySeparate()
    {
        var now = DateTimeOffset.Parse("2026-11-07T00:01:00Z");
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        FxQuote Quote(int days, decimal jpy) => new(now.AddDays(days), now, jpy, 15000m, today.AddDays(days));
        var daily = new[] { Quote(-8, 145), Quote(-7, 146), Quote(-1, 150), Quote(0, 151) };
        var hourly = new[] { new FxQuote(now.AddHours(-169), now, 149, 15000), new FxQuote(now.AddHours(-1), now, 151, 15000) };
        var latest = new FxQuote(now, now, 152.123456789m, 15200);
        var series = FxMath.Series(daily, hourly, latest, false, now);
        Assert.Equal(3, series.Length);
        Assert.Equal(today.AddDays(-7), series[0].Quote.HistoricalDate);
        Assert.Equal(TimeOnly.MaxValue, TimeOnly.FromDateTime(series[0].At.UtcDateTime));
        Assert.Null(series[^1].Quote.HistoricalDate);
        var range = FxMath.Range(series, "USD/JPY")!;
        Assert.Equal(146, range.Low); Assert.Equal(152.123456789m, range.High);
        Assert.Equal(6.123456789m, range.Change);
        Assert.Single(FxMath.Series(daily, hourly, latest, true, now));
        Assert.Equal(4, FxMath.Precision("JPY/IDR"));
        Assert.Empty(FxMath.Series([], [], null, false, now));
        Assert.Null(FxMath.Range([], "USD/JPY"));
    }

    [Fact]
    public async Task CacheRestoresWithoutCredentialsAndRemovePreservesIt()
    {
        await using var f = new Fixture();
        await f.Service.StartAsync(true, false); await f.Service.RefreshAsync();
        var latest = f.Service.State.Latest;
        await f.Service.RemoveKeyAsync();
        Assert.Null(f.Keys.Key); Assert.Equal(latest, f.Service.State.Latest);
        await f.Service.RefreshAsync(true); Assert.Equal(1, f.Provider.LatestCalls);
        using var restored = new FxRatesService(f.Provider, f.Keys, f.DirectoryPath, () => f.Now);
        await restored.StartAsync(true, false); await restored.RefreshAsync();
        Assert.Equal(latest, restored.State.Latest);
        Assert.Equal(7, restored.State.Daily.Length);
        Assert.Contains("Missing key", restored.State.Status);
        await restored.StopAsync();
    }

    [Fact]
    public async Task MissingDatesOnlyAreFetchedAfterUtcMidnight()
    {
        await using var f = new Fixture();
        await f.Service.StartAsync(true, false); await f.Service.RefreshAsync();
        f.Now = f.Now.AddDays(1); await f.Service.RefreshAsync();
        Assert.Equal(8, f.Provider.HistoryCalls);
        Assert.Equal(DateOnly.FromDateTime(f.Now.UtcDateTime).AddDays(-1), f.Service.State.Daily[^1].HistoricalDate);
    }

    [Fact]
    public async Task FailedAndTimedOutAttemptsConsumePersistedBudget()
    {
        await using var f = new Fixture(TimeSpan.FromMilliseconds(30));
        f.Provider.Latest = _ => new TaskCompletionSource<FxQuote>().Task;
        await f.Service.StartAsync(true, false); await f.Service.RefreshAsync();
        Assert.Contains("timed out", f.Service.State.Status);
        var cache = AtomicJsonStore.Load(f.FilePath, () => new FxCache()).Value;
        Assert.Single(cache.Requests);
        await f.Service.RefreshAsync(); Assert.Equal(1, f.Provider.LatestCalls);
        f.Now = f.Now.AddHours(1);
        f.Provider.Latest = _ => throw new HttpRequestException("fake failure");
        await f.Service.RefreshAsync();
        Assert.Equal(2, AtomicJsonStore.Load(f.FilePath, () => new FxCache()).Value.Requests.Count);
    }

    [Fact]
    public async Task CancelledOldConfigurationCannotPublishLateQuoteOrError()
    {
        await using var f = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource<FxQuote>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Provider.Latest = _ => { started.SetResult(); return late.Task; };
        await f.Service.StartAsync(true, false);
        var refresh = f.Service.RefreshAsync(); await started.Task;
        f.Service.Configure(false, false); await refresh;
        late.SetResult(f.Quote());
        await f.Service.StopAsync();
        Assert.Null(f.Service.State.Latest);
        Assert.Equal(1, f.Provider.LatestCalls);
        Assert.DoesNotContain("Failed", f.Service.State.Status);
    }

    [Fact]
    public async Task SingleFlightAndSuspensionPreventDuplicateOrHiddenRequests()
    {
        await using var f = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<FxQuote>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Provider.Latest = _ => { started.SetResult(); return result.Task; };
        await f.Service.StartAsync(true, false);
        var first = f.Service.RefreshAsync(); await started.Task;
        await f.Service.RefreshAsync(true); Assert.Equal(1, f.Provider.LatestCalls);
        result.SetResult(f.Quote()); await first;
        f.Service.Configure(true, true);
        f.Now = f.Now.AddHours(3); await f.Service.RefreshAsync(true);
        Assert.Equal(1, f.Provider.LatestCalls);
        f.Provider.Latest = _ => Task.FromResult(f.Quote());
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Service.Changed += () => { if (f.Service.State.Latest?.SourceAt == f.Now && f.Service.State.Status == "Ready") resumed.TrySetResult(); };
        f.Service.Configure(true, false);
        await resumed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, f.Provider.LatestCalls);
    }

    [Fact]
    public async Task KeyChangesDoNotResetManualThrottleOrLedger()
    {
        await using var f = new Fixture();
        await f.Service.StartAsync(true, false); await f.Service.RefreshAsync(true);
        await f.Service.SaveKeyAsync("different-fake");
        var calls = f.Provider.LatestCalls;
        await f.Service.RefreshAsync(true);
        Assert.Equal(calls, f.Provider.LatestCalls);
        Assert.Equal(calls + 7, AtomicJsonStore.Load(f.FilePath, () => new FxCache()).Value.Requests.Count);
    }

    [Fact]
    public async Task MalformedAndBackwardLatestQuotesDoNotOverwriteValidData()
    {
        await using var f = new Fixture();
        await f.Service.StartAsync(true, false); await f.Service.RefreshAsync();
        var valid = f.Service.State.Latest;
        f.Now = f.Now.AddHours(1);
        f.Provider.Latest = _ => Task.FromResult(f.Quote() with { Jpy = 0 });
        await f.Service.RefreshAsync(); Assert.Equal(valid, f.Service.State.Latest);
        f.Now = f.Now.AddHours(1);
        f.Provider.Latest = _ => Task.FromResult(f.Quote() with { SourceAt = valid!.SourceAt.AddHours(-1), Jpy = 1 });
        await f.Service.RefreshAsync(); Assert.Equal(valid, f.Service.State.Latest);
    }

    [Fact]
    public async Task InvalidHistoricalResultIsNotCached()
    {
        await using var f = new Fixture();
        f.Provider.History = (d, _) => Task.FromResult(f.Quote(d.AddDays(1)));
        await f.Service.StartAsync(true, false); await f.Service.RefreshAsync();
        Assert.NotNull(f.Service.State.Latest); Assert.Empty(f.Service.State.Daily);
        Assert.Contains("Failed", f.Service.State.Status);
    }

    [Theory]
    [InlineData("not_allowed", true)]
    [InlineData("quota_exceeded", false)]
    [InlineData("access_restricted", false)]
    public async Task ProviderSeparatesHistoryPermissionsFromQuota(string code, bool historyDenied)
    {
        var now = DateTimeOffset.UtcNow;
        using var http = new HttpClient(new Handler(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent(JsonSerializer.Serialize(new { error = true, message = code })) };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddMinutes(25));
            return response;
        }));
        var provider = new OpenExchangeRatesProvider(http, () => now);
        var error = await Assert.ThrowsAsync<FxProviderException>(() => provider.HistoryAsync("fake", DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1), default));
        Assert.Equal(historyDenied, error.HistoryDenied);
        Assert.True(error.Retry >= TimeSpan.FromMinutes(24));
    }

    [Fact]
    public async Task HistoryQuotaBackoffAlsoBlocksLatestRequests()
    {
        await using var f = new Fixture();
        f.Provider.History = (_, _) => throw new FxProviderException("Quota exhausted", TimeSpan.FromHours(2));
        await f.Service.StartAsync(true, false); await f.Service.RefreshAsync();
        Assert.NotNull(f.Service.State.Latest); Assert.Contains("Quota", f.Service.State.Status);
        f.Now = f.Now.AddHours(1); await f.Service.RefreshAsync(true);
        Assert.Equal(1, f.Provider.LatestCalls);
        f.Now = f.Now.AddHours(1); await f.Service.RefreshAsync();
        Assert.Equal(2, f.Provider.LatestCalls);
    }

    [Fact]
    public async Task DisabledFeatureNeverTestsOrTracksTheKey()
    {
        await using var f = new Fixture();
        await f.Service.StartAsync(false, false); await f.Service.SaveKeyAsync("fake-2");
        await f.Service.RefreshAsync(true); Assert.Equal(0, f.Provider.LatestCalls);
        Assert.Equal("fake-2", f.Keys.Key);
        await f.Service.StopAsync(); await f.Service.StopAsync();
    }

    [Fact]
    public async Task HttpInvalidQuotesAndHistoricalDatesAreRejected()
    {
        var now = DateTimeOffset.Parse("2026-11-06T12:00:00Z");
        foreach (var jpy in new[] { 0m, -1m, 150m })
        {
            using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { @base = "USD", timestamp = now.ToUnixTimeSeconds(), rates = new { JPY = jpy, IDR = 15000m } })) }));
            var provider = new OpenExchangeRatesProvider(http, () => now);
            await Assert.ThrowsAsync<InvalidDataException>(() => provider.HistoryAsync("fake", new DateOnly(2026, 11, 5), default));
        }
    }
}
