using MochiDock.Core;
using MochiDock.Services.DeviceBattery;

namespace MochiDock.Services;

/// <summary>
/// Polls wireless mouse/keyboard batteries every 60 s on a background task (single-flight, cancellable) while enabled and not suspended
/// (workstation locked or display off). Activation adds follow-up scans at 3, 10 and 25 s; device-change and resume events request a
/// debounced refresh; <see cref="RefreshNow"/> is the manual refresh.
/// </summary>
public sealed class DeviceBatteryService : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    readonly object gate = new();
    readonly Func<IReadOnlyList<DeviceBatteryReading>> scan;
    readonly Func<DateTimeOffset> clock;
    readonly System.Threading.Timer timer;
    CancellationTokenSource? debounce;
    CancellationTokenSource lifetime = new();
    IReadOnlyList<DeviceBatteryReading> latest = Array.Empty<DeviceBatteryReading>();
    bool enabled, suspended, disposed, running;
    Task? current;

    public event Action? Changed;

    /// <summary>Follow-up scans after activation (sequential delays): a receiver can enumerate before its wireless link is ready at sign-in.</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultStartupDelays = new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(15) };

    readonly IReadOnlyList<TimeSpan> startupDelays;
    CancellationTokenSource? startup;

    public DeviceBatteryService(Func<IReadOnlyList<DeviceBatteryReading>>? scan = null, Func<DateTimeOffset>? clock = null, IReadOnlyList<TimeSpan>? startupDelays = null)
    {
        this.scan = scan ?? DeviceBatteryScanner.PollOnce;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.startupDelays = startupDelays ?? DefaultStartupDelays;
        timer = new(_ => Trigger(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public IReadOnlyList<DeviceBatteryReading> Latest { get { lock (gate) return latest; } }

    public void Configure(bool isEnabled, bool isSuspended)
    {
        bool poll, clear = false;
        lock (gate)
        {
            if (disposed) return;
            var wasActive = enabled && !suspended;
            enabled = isEnabled;
            suspended = isSuspended;
            var active = enabled && !suspended;
            poll = active && !wasActive;
            if (!active) { timer.Change(Timeout.Infinite, Timeout.Infinite); CancelPending(); }
            else if (!wasActive) { timer.Change(PollInterval, PollInterval); ScheduleStartupPolls(); }
            if (!enabled && latest.Count > 0) { latest = Array.Empty<DeviceBatteryReading>(); clear = true; }
        }
        if (clear) Changed?.Invoke();
        if (poll) Trigger();
    }

    void CancelPending() { lifetime.Cancel(); lifetime.Dispose(); lifetime = new(); debounce?.Cancel(); debounce = null; startup?.Cancel(); startup = null; }

    // Caller holds the gate. Each delay follows the previous scan request; Trigger is single-flight, so overlapping scans cannot pile up.
    void ScheduleStartupPolls()
    {
        startup?.Cancel();
        var source = startup = new();
        var delays = startupDelays;
        _ = Task.Run(async () =>
        {
            foreach (var delay in delays)
            {
                try { await Task.Delay(delay, source.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                Trigger();
            }
        });
    }

    /// <summary>
    /// Scans now (manual refresh) and returns a task that completes when that scan has finished. Single-flight: while a scan is
    /// already running this joins it instead of starting another. Completes at once when disabled or suspended.
    /// </summary>
    public Task RefreshNow()
    {
        lock (gate)
        {
            if (disposed || !enabled || suspended) return Task.CompletedTask;
            if (running) return current ?? Task.CompletedTask;
        }
        Trigger();
        lock (gate) return current ?? Task.CompletedTask;
    }

    /// <summary>Refreshes after <paramref name="delay"/>; repeated requests restart the delay.</summary>
    public void RequestRefresh(TimeSpan delay)
    {
        CancellationTokenSource source;
        lock (gate)
        {
            if (disposed || !enabled || suspended) return;
            debounce?.Cancel();
            source = debounce = new();
        }
        _ = Task.Delay(delay, source.Token).ContinueWith(t => { if (!t.IsCanceled) Trigger(); }, TaskScheduler.Default);
    }

    void Trigger()
    {
        CancellationToken token;
        lock (gate)
        {
            if (disposed || !enabled || suspended || running) return;
            running = true;
            token = lifetime.Token;
            current = Task.Run(() => Poll(token));
        }
    }

    void Poll(CancellationToken token)
    {
        try
        {
            var polled = scan();
            bool changed;
            lock (gate)
            {
                if (token.IsCancellationRequested || disposed || !enabled || suspended) return;
                var merged = DeviceBatteryMerge.Merge(latest, polled, clock());
                changed = !merged.SequenceEqual(latest);
                latest = merged;
            }
            if (changed) Changed?.Invoke();
        }
        catch (Exception ex) { AppLog.Warn("DeviceBattery", ex); }
        finally { lock (gate) running = false; }
    }

    public Task StopAsync()
    {
        Task? pending;
        lock (gate)
        {
            enabled = false;
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            CancelPending();
            pending = current;
        }
        return pending ?? Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        timer.Dispose();
        lock (gate) { CancelPending(); }
    }
}
