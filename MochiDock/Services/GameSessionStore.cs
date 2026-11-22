using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>
/// The history of finished game sessions: newest first, at most <see cref="GameSessionFormat.HistoryCap"/>, kept in
/// %LocalAppData%\MochiDock\GameSessions.json. Saves are queued and atomic, like the other JSON stores; an unreadable file
/// is preserved by <see cref="AtomicJsonStore"/> and the history starts empty.
/// </summary>
public sealed class GameSessionStore
{
    private readonly string _path;
    private readonly QueuedJsonWriter _writer = new();
    private List<GameSessionRecord> _sessions;

    public event Action<string>? PersistenceError;

    public GameSessionStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppDataPaths.Local, "GameSessions.json");
        _sessions = AtomicJsonStore.Load(_path, () => new List<GameSessionRecord>()).Value ?? new List<GameSessionRecord>();
    }

    public IReadOnlyList<GameSessionRecord> Sessions => _sessions;

    public IReadOnlyList<GameSessionRecord> Add(GameSessionRecord record)
    {
        _sessions = GameSessionFormat.AddToHistory(_sessions, record);
        Save();
        return _sessions;
    }

    /// <summary>Saves the history again after a stored record changed (for example a match that arrived later).</summary>
    public void Update() => Save();

    public void Clear()
    {
        _sessions = new List<GameSessionRecord>();
        Save();
    }

    public Task FlushAsync() => _writer.FlushAsync();

    private void Save() =>
        _writer.Enqueue(_path, _sessions, msg => PersistenceError?.Invoke(msg));
}
