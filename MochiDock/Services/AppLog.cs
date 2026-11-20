using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MochiDock.Services;

/// <summary>
/// Small, dependency-free diagnostic log. It exists so "best effort" code paths stop failing silently:
/// failures go to a size-capped rolling file under %LocalAppData%\MochiDock\logs and to a short in-memory list
/// the Settings page can show. It never throws, de-duplicates repeats (polling loops would otherwise flood it),
/// ignores cancellation and redacts anything that looks like a secret.
/// </summary>
public static class AppLog
{
    public sealed record LogEntry(DateTimeOffset Time, string Level, string Area, string Message);

    private const long MaxFileBytes = 512 * 1024;
    private const int RecentCapacity = 40;
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(1);
    private static readonly Regex BearerPattern = new(@"(?i)(bearer\s+)[^\s&;,""']+", RegexOptions.Compiled);
    private static readonly Regex SecretPattern = new(@"(?i)((?:api[_-]?key|key|token|secret|password|authorization)\s*[=:]\s*)[^\s&;,""']+", RegexOptions.Compiled);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTimeOffset> LastSeen = new();
    private static readonly LinkedList<LogEntry> RecentEntries = new();

    private static string _directory = Path.Combine(AppDataPaths.Local, "logs");
    private static Func<DateTimeOffset> _clock = () => DateTimeOffset.Now;
    private static int _warningCount;

    public static string Directory
    {
        get { lock (Gate) return _directory; }
    }

    /// <summary>Warnings and errors recorded since start (duplicates inside the de-duplication window are not counted twice).</summary>
    public static int WarningCount
    {
        get { lock (Gate) return _warningCount; }
    }

    /// <summary>Redirects the log (tests) and resets all state.</summary>
    internal static void Configure(string directory, Func<DateTimeOffset>? clock = null)
    {
        lock (Gate)
        {
            _directory = directory;
            _clock = clock ?? (() => DateTimeOffset.Now);
            LastSeen.Clear();
            RecentEntries.Clear();
            _warningCount = 0;
        }
    }

    public static IReadOnlyList<LogEntry> Recent()
    {
        lock (Gate) return new List<LogEntry>(RecentEntries);
    }

    public static void Warn(string area, Exception exception, [CallerMemberName] string? member = null) =>
        Write("WARN", area, Describe(exception, member), exception);

    public static void Warn(string area, string message) => Write("WARN", area, message, null);

    public static void Error(string area, Exception exception, [CallerMemberName] string? member = null) =>
        Write("ERROR", area, Describe(exception, member), exception);

    public static void Error(string area, string message) => Write("ERROR", area, message, null);

    internal static string Redact(string text) =>
        string.IsNullOrEmpty(text) ? text : SecretPattern.Replace(BearerPattern.Replace(text, "$1<redacted>"), "$1<redacted>");

    private static string Describe(Exception exception, string? member)
    {
        var where = string.IsNullOrEmpty(member) ? "" : member + ": ";
        return where + exception.GetType().Name + ": " + exception.Message;
    }

    private static void Write(string level, string area, string message, Exception? exception)
    {
        try
        {
            // Cancellation is how work is normally stopped; it is not a failure.
            if (exception is OperationCanceledException) return;

            message = Redact(message).Replace('\r', ' ').Replace('\n', ' ');
            lock (Gate)
            {
                var now = _clock();
                var key = area + "|" + message;
                if (LastSeen.TryGetValue(key, out var seen) && now - seen < DuplicateWindow) return;
                if (LastSeen.Count > 500) LastSeen.Clear();
                LastSeen[key] = now;

                _warningCount++;
                var entry = new LogEntry(now, level, area, message);
                RecentEntries.AddLast(entry);
                while (RecentEntries.Count > RecentCapacity) RecentEntries.RemoveFirst();

                System.IO.Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, "mochidock.log");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxFileBytes)
                {
                    var old = Path.Combine(_directory, "mochidock.1.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }

                var line = new StringBuilder()
                    .Append(now.ToString("yyyy-MM-dd HH:mm:ss"))
                    .Append(' ').Append(level)
                    .Append(" [").Append(area).Append("] ")
                    .Append(message)
                    .AppendLine()
                    .ToString();
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break the feature it is reporting on.
        }
    }
}
