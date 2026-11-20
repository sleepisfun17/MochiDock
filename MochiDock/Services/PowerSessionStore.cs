using System.IO;
using System.Text.Json;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>
/// The history of power sessions (newest first, at most <see cref="PowerSessionFormat.HistoryCap"/>) in
/// %LocalAppData%\MochiDock\PowerSessions.json. Saves are queued and atomic like the other JSON stores; an unreadable file is
/// preserved by <see cref="AtomicJsonStore"/> and the history starts empty. A record still open at startup belonged to a run that
/// did not exit cleanly (crash, power loss): it is closed at its last checkpoint and flagged.
/// </summary>
public sealed class PowerSessionStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Task _pending = Task.CompletedTask;
    private List<PowerSessionRecord> _sessions;

    public event Action<string>? PersistenceError;

    public PowerSessionStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppDataPaths.Local, "PowerSessions.json");
        _sessions = AtomicJsonStore.Load(_path, () => new List<PowerSessionRecord>()).Value ?? new List<PowerSessionRecord>();
        var recovered = false;
        foreach (var record in _sessions.Where(r => r.Open))
        {
            record.Open = false;
            record.EndedUnexpectedly = true;
            recovered = true;
        }
        if (recovered) Save();
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<PowerSessionRecord> Sessions { get { lock (_gate) return _sessions.ToList(); } }

    /// <summary>Adds the record, or replaces the one with the same id (the running session is saved over and over as it grows).</summary>
    public void Upsert(PowerSessionRecord record)
    {
        lock (_gate) _sessions = PowerSessionFormat.AddToHistory(_sessions, record);
        Save();
    }

    public void Clear()
    {
        lock (_gate) _sessions = new List<PowerSessionRecord>();
        Save();
    }

    public Task FlushAsync() { lock (_gate) return _pending; }

    private void Save()
    {
        string snapshot;
        lock (_gate) snapshot = JsonSerializer.Serialize(_sessions, new JsonSerializerOptions { WriteIndented = true });
        lock (_gate)
            _pending = _pending.ContinueWith(_ =>
            {
                try { AtomicJsonStore.SaveJson(_path, snapshot); }
                catch (Exception ex) { PersistenceError?.Invoke(ex.Message); }
            }, TaskScheduler.Default);
    }
}
