using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>
/// The 24-hour Stats history (<see cref="StatsHistory"/>, one bucket per minute) in %LocalAppData%\MochiDock\StatsHistory.json.
/// Samples are added on the UI thread; saves are queued and atomic like the other JSON stores and happen every five minutes, on
/// suspend and on exit. An unreadable file is preserved by <see cref="AtomicJsonStore"/> and the history starts empty.
/// </summary>
public sealed class StatsHistoryService
{
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly StatsHistory _history;
    private Task _pending = Task.CompletedTask;
    private DateTimeOffset _lastSave = DateTimeOffset.UtcNow;

    public event Action<string>? PersistenceError;

    public StatsHistoryService(string? path = null)
    {
        _path = path ?? Path.Combine(AppDataPaths.Local, "StatsHistory.json");
        var loaded = AtomicJsonStore.Load(_path, () => new List<HistoryBucket>()).Value ?? new List<HistoryBucket>();
        _history = new StatsHistory(loaded);
        _history.Trim(DateTimeOffset.UtcNow);
    }

    public string FilePath => _path;

    /// <summary>Adds a sample, trims to 24 hours and saves when <see cref="SaveEvery"/> has passed.</summary>
    public void Add(HistorySample sample)
    {
        lock (_gate)
        {
            _history.Add(sample);
            _history.Trim(sample.At);
        }
        if (sample.At - _lastSave >= SaveEvery) Save(sample.At);
    }

    /// <summary>Runs <paramref name="read"/> on the history under the lock (chart and peak helpers).</summary>
    public T Read<T>(Func<StatsHistory, T> read) { lock (_gate) return read(_history); }

    public int Count { get { lock (_gate) return _history.Count; } }

    public void Clear()
    {
        lock (_gate) _history.Clear();
        Save(DateTimeOffset.UtcNow);
    }

    /// <summary>Saves now (suspend, exit) and returns the queued write.</summary>
    public Task FlushAsync()
    {
        Save(DateTimeOffset.UtcNow);
        lock (_gate) return _pending;
    }

    private void Save(DateTimeOffset now)
    {
        string snapshot;
        lock (_gate)
        {
            _lastSave = now;
            snapshot = JsonSerializer.Serialize(_history.Buckets, Json);
            _pending = _pending.ContinueWith(_ =>
            {
                try { AtomicJsonStore.SaveJson(_path, snapshot); }
                catch (Exception ex) { PersistenceError?.Invoke(ex.Message); }
            }, TaskScheduler.Default);
        }
    }
}
