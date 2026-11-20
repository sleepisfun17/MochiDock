using System;
using System.Collections.Generic;
using System.IO;

namespace MochiDock.Core;

/// <summary>Pure lookup of optional external tools (ffmpeg) so the Downloads card can fail with a clear message.</summary>
public static class ToolLocator
{
    public const string FfmpegMissingMessage =
        "ffmpeg is required for MP3 audio. Put ffmpeg.exe in the app's Tools folder, in %LocalAppData%\\MochiDock\\Tools, or on your PATH, then try again.";

    /// <summary>Candidate paths in priority order: app Tools folder, per-user Tools folder, then each PATH directory.</summary>
    public static IEnumerable<string> FfmpegCandidates(string appDir, string localAppData, string? pathEnv)
    {
        if (!string.IsNullOrWhiteSpace(appDir)) yield return Path.Combine(appDir, "Tools", "ffmpeg.exe");
        if (!string.IsNullOrWhiteSpace(localAppData)) yield return Path.Combine(localAppData, "MochiDock", "Tools", "ffmpeg.exe");
        if (string.IsNullOrWhiteSpace(pathEnv)) yield break;
        foreach (var raw in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = raw.Trim().Trim('"');
            if (dir.Length == 0) continue;
            yield return Path.Combine(dir, "ffmpeg.exe");
        }
    }

    /// <summary>Returns the first existing ffmpeg.exe, or null when none is installed.</summary>
    public static string? FindFfmpeg(string appDir, string localAppData, string? pathEnv, Func<string, bool> fileExists)
    {
        foreach (var candidate in FfmpegCandidates(appDir, localAppData, pathEnv))
        {
            try { if (fileExists(candidate)) return candidate; }
            catch (ArgumentException) { /* malformed PATH entry */ }
        }
        return null;
    }
}
