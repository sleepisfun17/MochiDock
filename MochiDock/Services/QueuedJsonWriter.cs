using System.Text.Json;

namespace MochiDock.Services;

/// <summary>Serializes snapshots with a cached <see cref="JsonSerializerOptions"/> and writes them sequentially through <see cref="AtomicJsonStore"/>.</summary>
public sealed class QueuedJsonWriter
{
    public static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private Task _pending = Task.CompletedTask;

    public void Enqueue<T>(string path, T value, Action<string>? onError = null) =>
        EnqueueJson(path, JsonSerializer.Serialize(value, IndentedOptions), onError);

    public void EnqueueJson(string path, string json, Action<string>? onError = null)
    {
        lock (_gate)
            _pending = _pending.ContinueWith(_ =>
            {
                try { AtomicJsonStore.SaveJson(path, json); }
                catch (Exception ex) { onError?.Invoke(ex.Message); }
            }, TaskScheduler.Default);
    }

    public Task FlushAsync() { lock (_gate) return _pending; }
}
