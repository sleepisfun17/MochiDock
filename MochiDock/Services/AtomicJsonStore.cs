using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;

namespace MochiDock.Services;

public enum JsonLoadStatus { Missing, Loaded, Recovered, Unreadable, Invalid }
public sealed record JsonLoadResult<T>(T Value, JsonLoadStatus Status, bool CanWrite, string? Error);

public static class AtomicJsonStore
{
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Blocked = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static event Action<string>? Error;
    private static object Gate(string path) => Gates.GetOrAdd(Path.GetFullPath(path), _ => new());

    public static JsonLoadResult<T> Load<T>(string path, Func<T> defaults)
    {
        path = Path.GetFullPath(path);
        lock (Gate(path))
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                T value = JsonSerializer.Deserialize<T>(stream) ?? throw new JsonException("JSON root cannot be null.");
                Blocked.TryRemove(path, out _);
                return new(value, JsonLoadStatus.Loaded, true, null);
            }
            catch (FileNotFoundException) { return new(defaults(), JsonLoadStatus.Missing, true, null); }
            catch (DirectoryNotFoundException) { return new(defaults(), JsonLoadStatus.Missing, true, null); }
            catch (JsonException ex)
            {
                try
                {
                    string preserved = path + ".corrupt-" + Guid.NewGuid().ToString("N");
                    File.Move(path, preserved);
                    T? backup = default;
                    try { backup = JsonSerializer.Deserialize<T>(File.ReadAllText(path + ".bak")); } catch { }
                    Blocked.TryRemove(path, out _);
                    string message = $"Invalid JSON preserved at {preserved}: {ex.Message}";
                    Error?.Invoke(message);
                    return new(backup is null ? defaults() : backup, backup is null ? JsonLoadStatus.Invalid : JsonLoadStatus.Recovered, true, message);
                }
                catch (Exception preserveError) when (preserveError is IOException or UnauthorizedAccessException)
                { return Unreadable(path, defaults(), preserveError); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Unreadable(path, defaults(), ex); }
        }
    }

    private static JsonLoadResult<T> Unreadable<T>(string path, T value, Exception error)
    {
        Blocked[path] = 0;
        string message = $"Cannot read {path}; automatic writes are blocked: {error.Message}";
        Error?.Invoke(message);
        return new(value, JsonLoadStatus.Unreadable, false, message);
    }

    public static void SaveJson(string path, string json, bool explicitImport = false)
    {
        using var valid = JsonDocument.Parse(json);
        path = Path.GetFullPath(path);
        lock (Gate(path))
        {
            if (Blocked.ContainsKey(path) && !explicitImport) throw new IOException($"Automatic writes blocked for unreadable file: {path}");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(file, leaveOpen: true))
                { writer.Write(json); writer.Flush(); file.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                Blocked.TryRemove(path, out _);
            }
            catch (Exception ex) { Error?.Invoke($"Cannot save {path}: {ex.Message}"); throw; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public static void Save<T>(string path, T value) => SaveJson(path, JsonSerializer.Serialize(value, Options));
}
