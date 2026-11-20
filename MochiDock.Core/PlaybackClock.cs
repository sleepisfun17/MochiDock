namespace MochiDock.Core;

public readonly record struct PlaybackSnapshot(double Position, double Duration, double SampledAt, bool Playing, double Rate = 1)
{
    public double At(double now) => Math.Clamp(Position + (Playing ? Math.Max(0, now - SampledAt) * (double.IsFinite(Rate) && Rate > 0 ? Rate : 1) : 0), 0, Duration > 0 ? Duration : double.MaxValue);
    public static double ValidAge(double age) => double.IsFinite(age) && age >= 0 && age <= 3600 ? age : 0;
}

public sealed class LyricClock
{
    double position;
    double? last;
    public void Reset() => last = null;
    public double Tick(double now, double report, bool playing, double rate)
    {
        double dt = last.HasValue ? now - last.Value : -1;
        last = now;
        if (!playing || dt < 0 || dt > .5) return position = report;
        double predicted = position + dt * (double.IsFinite(rate) && rate > 0 ? rate : 1);
        double error = report - predicted;
        return position = Math.Abs(error) > 1 ? report : Math.Max(position, predicted + error * (1 - Math.Exp(-dt / .6)));
    }
}

public readonly record struct PendingMediaSeek(double Target, double At)
{
    public bool Holds(PlaybackSnapshot report, double now) => now - At < 3 && Math.Abs(report.At(now) - (Target + (report.Playing ? Math.Max(0, now - At) * report.Rate : 0))) > 1.5;
}

public enum MediaSeekResult { Accepted, Unsupported, Rejected, TimedOut, Cancelled }
public static class MediaPresentation
{
    public static string TitleArtist(string title, string artist) => string.IsNullOrWhiteSpace(artist) ? title : $"{title} – {artist}";
    public static int ActiveLine<T>(IReadOnlyList<T> lines, Func<T, double> timestamp, double position)
    {
        int index = -1;
        for (int i = 0; i < lines.Count && timestamp(lines[i]) <= position; i++) index = i;
        return index;
    }
}
