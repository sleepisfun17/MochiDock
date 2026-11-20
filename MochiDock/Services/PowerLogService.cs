using MochiDock.Core;
using Timer = System.Threading.Timer;

namespace MochiDock.Services;

/// <summary>
/// Logs the PC's total power in the background: one sample about every 10 s while enabled, added up into a session by
/// <see cref="PowerSessionTracker"/> and saved by <see cref="PowerSessionStore"/> (a checkpoint every 2 minutes, a final save on exit,
/// shutdown or restart). Sampling is cheap: the sensor reader never blocks and nothing runs while logging is off.
/// </summary>
public sealed class PowerLogService : IDisposable
{
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan CheckpointInterval = TimeSpan.FromMinutes(2);

    private readonly PowerSessionStore _store;
    private readonly object _gate = new();
    private Func<PowerReading?>? _sample;
    private Timer? _timer;
    private PowerSessionTracker? _tracker;
    private DateTimeOffset _lastCheckpoint;
    private bool _disposed;

    public PowerLogService(PowerSessionStore? store = null) => _store = store ?? new PowerSessionStore();

    /// <summary>Raised on a background thread after a sample was added (marshal to the dispatcher before touching UI).</summary>
    public event Action? Updated;

    /// <summary>The running session so far, or null when logging is off or nothing was sampled yet.</summary>
    public PowerSessionRecord? Current
    {
        get { lock (_gate) return _tracker is { HasData: true } t ? t.Snapshot(DateTimeOffset.UtcNow, true) : null; }
    }

    /// <summary>Finished sessions, newest first (the running one is not included).</summary>
    public IReadOnlyList<PowerSessionRecord> History
    {
        get
        {
            Guid? currentId;
            lock (_gate) currentId = _tracker?.Snapshot(DateTimeOffset.UtcNow, true).Id;
            return _store.Sessions.Where(r => !r.Open && r.Id != currentId).ToList();
        }
    }

    public event Action<string>? PersistenceError
    {
        add => _store.PersistenceError += value;
        remove => _store.PersistenceError -= value;
    }

    /// <summary>Starts or stops logging. Turning it off closes the running session; the history stays.</summary>
    public void Configure(bool enabled, Func<PowerReading?> sample)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _sample = sample;
            if (enabled && _timer is null)
            {
                _tracker = new PowerSessionTracker(DateTimeOffset.UtcNow);
                _lastCheckpoint = DateTimeOffset.UtcNow;
                _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), SampleInterval);
            }
            else if (!enabled && _timer is not null) StopLocked();
        }
    }

    private void Tick()
    {
        try
        {
            if (!Monitor.TryEnter(_gate)) return;
            try
            {
                if (_disposed || _tracker is null || _sample is null) return;
                var now = DateTimeOffset.UtcNow;
                if (_sample() is { } reading) _tracker.Add(now, reading.Watts, reading.Source);
                if (_tracker.HasData && now - _lastCheckpoint >= CheckpointInterval) CheckpointLocked(now);
            }
            finally { Monitor.Exit(_gate); }
            Updated?.Invoke();
        }
        catch (Exception ex) { AppLog.Warn("PowerLog", ex); }
    }

    /// <summary>Saves the running session now (suspend, session ending).</summary>
    public void Checkpoint()
    {
        lock (_gate) if (_tracker is { HasData: true }) CheckpointLocked(DateTimeOffset.UtcNow);
    }

    private void CheckpointLocked(DateTimeOffset now)
    {
        _store.Upsert(_tracker!.Snapshot(now, true));
        _lastCheckpoint = now;
    }

    /// <summary>Stops sampling, closes the running session and writes it. Safe to call more than once.</summary>
    public Task StopAsync()
    {
        lock (_gate) StopLocked();
        return _store.FlushAsync();
    }

    private void StopLocked()
    {
        _timer?.Dispose();
        _timer = null;
        if (_tracker is { HasData: true } tracker) _store.Upsert(tracker.Snapshot(DateTimeOffset.UtcNow, false));
        _tracker = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            StopLocked();
            _disposed = true;
        }
    }
}
