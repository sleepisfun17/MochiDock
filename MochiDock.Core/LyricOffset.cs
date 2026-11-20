using System.Globalization;

namespace MochiDock.Core;

/// <summary>
/// User-adjustable lyric timing. Positive values make lines appear EARLIER (the position used for lookup is pushed forward).
/// Applied only when choosing the active line: the playback clock, progress bar and seek are never shifted.
/// </summary>
public static class LyricOffset
{
    public const int MaxMs = 5000;
    public const int StepMs = 100;

    public static int Clamp(int offsetMs) => Math.Clamp(offsetMs, -MaxMs, MaxMs);

    /// <summary>The playback position, in seconds, to look lyric lines up with.</summary>
    public static double Apply(double positionSeconds, int offsetMs) =>
        double.IsFinite(positionSeconds) ? Math.Max(0, positionSeconds + Clamp(offsetMs) / 1000.0) : 0;

    public static string Label(int offsetMs)
    {
        int ms = Clamp(offsetMs);
        if (ms == 0) return "On time";
        string seconds = (Math.Abs(ms) / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
        return ms > 0 ? $"Earlier {seconds}s" : $"Later {seconds}s";
    }
}
