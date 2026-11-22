using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>
/// The playtime ledger in %LocalAppData%\MochiDock\GamePlaytime.json. Saves are queued and atomic like the other JSON stores; an
/// unreadable file is preserved by <see cref="AtomicJsonStore"/> and the ledger starts empty. Days older than 90 are dropped on load and save.
/// </summary>
public sealed class GamePlaytimeStore
{
    private readonly string _path;
    private readonly QueuedJsonWriter _writer = new();

    public GamePlaytimeLedger Ledger { get; private set; }
    public event Action<string>? PersistenceError;

    public GamePlaytimeStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppDataPaths.Local, "GamePlaytime.json");
        Ledger = AtomicJsonStore.Load(_path, () => new GamePlaytimeLedger()).Value ?? new GamePlaytimeLedger();
        Ledger.Prune(DateTime.Now);
    }

    /// <summary>Adds a finished stretch of play (local times) and saves.</summary>
    public void Add(string? game, DateTime start, DateTime end)
    {
        Ledger.Add(game, start, end);
        Ledger.Prune(end);
        Save();
    }

    public void Clear()
    {
        Ledger = new GamePlaytimeLedger();
        Save();
    }

    public Task FlushAsync() => _writer.FlushAsync();

    private void Save() =>
        _writer.Enqueue(_path, Ledger, msg => PersistenceError?.Invoke(msg));
}
