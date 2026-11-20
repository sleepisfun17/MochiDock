using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MochiDock.Core;

/// <summary>The statistics of one finished game session.</summary>
public sealed record GameSessionResult(
    int Frames, double Seconds, double AverageFps, double Low1Fps, double Low01Fps, double MinFps, double MaxFps, int Stutters);

/// <summary>A finished session as it is stored and shown.</summary>
public sealed class GameSessionRecord
{
    public string Game { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public double Seconds { get; set; }
    public int Frames { get; set; }
    public double AverageFps { get; set; }
    public double Low1Fps { get; set; }
    public double Low01Fps { get; set; }
    public double MinFps { get; set; }
    public double MaxFps { get; set; }
    public int Stutters { get; set; }
    /// <summary>Average GPU load over the session in percent, or null when no hardware samples were taken.</summary>
    public double? GpuAveragePercent { get; set; }
    /// <summary>Highest measured GPU temperature in °C, or null when the temperature was never measured (estimates are not counted).</summary>
    public double? GpuPeakTemp { get; set; }
    /// <summary>Highest GPU load over the session in percent, or null when no hardware samples were taken.</summary>
    public double? GpuPeakPercent { get; set; }
    /// <summary>Average CPU load over the session in percent, or null when no hardware samples were taken.</summary>
    public double? CpuAveragePercent { get; set; }
    /// <summary>Highest CPU load over the session in percent, or null when no hardware samples were taken.</summary>
    public double? CpuPeakPercent { get; set; }
    /// <summary>Highest measured CPU temperature in °C, or null when it was never measured (the load-based estimate is not counted).</summary>
    public double? CpuPeakTemp { get; set; }
    /// <summary>Ping numbers to the configured host; null when fewer than 5 pings were answered.</summary>
    public double? PingAverage { get; set; }
    public double? PingWorst { get; set; }
    public double? PingJitter { get; set; }
    public int PingSpikes { get; set; }
    public int PingLost { get; set; }
    /// <summary>Graph points of the session; kept only for the newest sessions (see <see cref="GameSessionFormat.TimelinesKept"/>).</summary>
    public List<GameTimelinePoint>? Timeline { get; set; }
    public double TimelineBucketSeconds { get; set; }
    /// <summary>How this session compares with the usual ones of the same game; filled for display, never stored.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string TrendText { get; set; } = "";
    /// <summary>The match result and numbers the game's own API reported (League of Legends), or null.</summary>
    public GameMatchSummary? Match { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string RowToolTip => (Match != null ? GameMatchFormat.Line(Match) + Environment.NewLine : "") + "Average FPS / 1% low" + (TrendText.Length > 0 ? Environment.NewLine + TrendText : "");

    public void ApplyPing(PingResult? ping)
    {
        if (ping == null) return;
        PingAverage = ping.Average; PingWorst = ping.Worst; PingJitter = ping.Jitter; PingSpikes = ping.Spikes; PingLost = ping.Lost;
    }

    public void ApplyTimeline(GameSessionTimeline timeline)
    {
        var points = timeline.Points();
        Timeline = points.Count >= 2 ? points : null;
        TimelineBucketSeconds = timeline.BucketSeconds;
    }

    public static GameSessionRecord From(string game, DateTime startedAt, GameSessionResult r) => new()
    {
        Game = game, StartedAt = startedAt, Seconds = r.Seconds, Frames = r.Frames, AverageFps = r.AverageFps,
        Low1Fps = r.Low1Fps, Low01Fps = r.Low01Fps, MinFps = r.MinFps, MaxFps = r.MaxFps, Stutters = r.Stutters,
    };
}

/// <summary>
/// Collects the frame times of one session in a fixed histogram (0.1 ms buckets up to 200 ms), so memory stays constant for
/// multi-hour sessions and the percentiles are accurate to 0.1 ms. A gap of one second or more is a pause (alt-tab, loading
/// screen), not a frame, and is left out.
/// </summary>
public sealed class GameSessionStats
{
    public const double BucketMs = 0.1;
    public const int Buckets = 2000;           // buckets 0..1999 hold 0..200 ms; the last bucket collects everything slower
    public const double PauseMs = 1000;
    public const int MinFrames = 300;
    public const double MinSeconds = 60;

    private readonly long[] _histogram = new long[Buckets + 1];
    private long _frames;
    private double _sumMs, _minMs = double.MaxValue, _maxMs;
    private double _overflowSumMs;

    public int Frames => (int)Math.Min(_frames, int.MaxValue);
    public double Seconds => _sumMs / 1000.0;

    /// <summary>Adds one frame time. Returns false when it was ignored (not positive, not a number, or a pause).</summary>
    public bool Add(double frameMs)
    {
        if (!double.IsFinite(frameMs) || frameMs <= 0 || frameMs >= PauseMs) return false;
        int bucket = Math.Min((int)(frameMs / BucketMs), Buckets);
        _histogram[bucket]++;
        if (bucket == Buckets) _overflowSumMs += frameMs;
        _frames++;
        _sumMs += frameMs;
        if (frameMs < _minMs) _minMs = frameMs;
        if (frameMs > _maxMs) _maxMs = frameMs;
        return true;
    }

    /// <summary>The statistics, or null when the session was too short (under 300 frames or 60 seconds) unless minimums are not enforced.</summary>
    public GameSessionResult? Result(bool enforceMinimums = true)
    {
        if (_frames == 0) return null;
        if (enforceMinimums && (_frames < MinFrames || Seconds < MinSeconds)) return null;

        double median = Representative(MedianBucket());
        long stutters = 0;
        for (int i = 0; i <= Buckets; i++)
            if (_histogram[i] > 0 && IsStutter(Representative(i), median)) stutters += _histogram[i];

        return new GameSessionResult(
            Frames: (int)Math.Min(_frames, int.MaxValue),
            Seconds: Seconds,
            AverageFps: _frames / Seconds,
            Low1Fps: LowFps(0.01),
            Low01Fps: LowFps(0.001),
            MinFps: 1000.0 / _maxMs,
            MaxFps: 1000.0 / _minMs,
            Stutters: (int)Math.Min(stutters, int.MaxValue));
    }

    /// <summary>A frame must be slower than this to count as a stutter, however fast the game usually runs.</summary>
    public const double StutterFloorMs = 20;

    /// <summary>
    /// A stutter is a frame slower than twice the median frame time and also slower than <see cref="StutterFloorMs"/>: at 600 FPS the
    /// median is under 2 ms, and a 4 ms frame is normal jitter nobody can see, so the floor keeps the count and the graph meaningful.
    /// </summary>
    public static bool IsStutter(double frameMs, double medianMs) => frameMs > 2 * medianMs && frameMs > StutterFloorMs;

    // The frame time a bucket stands for: its middle, or the real mean for the "slower than 200 ms" bucket.
    private double Representative(int bucket) =>
        bucket == Buckets ? (_histogram[Buckets] > 0 ? _overflowSumMs / _histogram[Buckets] : Buckets * BucketMs)
                          : (bucket + 0.5) * BucketMs;

    private int MedianBucket()
    {
        long half = (_frames + 1) / 2, seen = 0;
        for (int i = 0; i <= Buckets; i++)
        {
            seen += _histogram[i];
            if (seen >= half) return i;
        }
        return Buckets;
    }

    // "1% low": the average FPS of the slowest 1% of frames (at least one frame).
    private double LowFps(double fraction)
    {
        long wanted = Math.Max(1, (long)Math.Ceiling(_frames * fraction - 1e-9));
        long taken = 0;
        double sumMs = 0;
        for (int i = Buckets; i >= 0 && taken < wanted; i--)
        {
            long take = Math.Min(_histogram[i], wanted - taken);
            if (take <= 0) continue;
            sumMs += take * Representative(i);
            taken += take;
        }
        return taken == 0 ? 0 : 1000.0 / (sumMs / taken);
    }
}

/// <summary>Averages the GPU load and tracks the peak measured GPU temperature over one session.</summary>
public sealed class GameSessionHardware
{
    private double _loadSum, _gpuPeak;
    private int _loadCount;
    private double _cpuSum;
    private double _peakTemp;
    private bool _tempSeen;
    private double _cpuPeak;
    private int _cpuCount;
    private double _cpuPeakTemp;
    private bool _cpuTempSeen;

    public void Add(double gpuPercent, double gpuTemp, bool tempMeasured, double cpuPercent = double.NaN, double cpuTemp = 0, bool cpuTempMeasured = false)
    {
        if (double.IsFinite(cpuPercent) && cpuPercent >= 0) { _cpuPeak = Math.Max(_cpuPeak, Math.Min(cpuPercent, 100)); _cpuSum += Math.Min(cpuPercent, 100); _cpuCount++; }
        if (cpuTempMeasured && double.IsFinite(cpuTemp) && cpuTemp > 0 && (!_cpuTempSeen || cpuTemp > _cpuPeakTemp)) { _cpuPeakTemp = cpuTemp; _cpuTempSeen = true; }
        if (double.IsFinite(gpuPercent) && gpuPercent >= 0) { _loadSum += Math.Min(gpuPercent, 100); _gpuPeak = Math.Max(_gpuPeak, Math.Min(gpuPercent, 100)); _loadCount++; }
        if (tempMeasured && double.IsFinite(gpuTemp) && gpuTemp > 0 && (!_tempSeen || gpuTemp > _peakTemp)) { _peakTemp = gpuTemp; _tempSeen = true; }
    }

    public double? AveragePercent => _loadCount >= 3 ? _loadSum / _loadCount : null;
    public double? PeakTemp => _tempSeen ? _peakTemp : null;
    public double? GpuPeakPercent => _loadCount >= 3 ? _gpuPeak : null;
    public double? CpuAveragePercent => _cpuCount >= 3 ? _cpuSum / _cpuCount : null;
    public double? CpuPeakPercent => _cpuCount >= 3 ? _cpuPeak : null;
    public double? CpuPeakTemp => _cpuTempSeen ? _cpuPeakTemp : null;

    public void Apply(GameSessionRecord record)
    {
        record.GpuAveragePercent = AveragePercent;
        record.GpuPeakTemp = PeakTemp;
        record.GpuPeakPercent = GpuPeakPercent;
        record.CpuAveragePercent = CpuAveragePercent;
        record.CpuPeakPercent = CpuPeakPercent;
        record.CpuPeakTemp = CpuPeakTemp;
    }
}

/// <summary>Text for session records and the capped, newest-first history.</summary>
public static class GameSessionFormat
{
    public const int HistoryCap = 50;
    /// <summary>Only the newest sessions keep their graph points, which bounds the size of the history file.</summary>
    public const int TimelinesKept = 10;

    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{(int)t.TotalMinutes}m {t.Seconds:00}s";
    }

    /// <summary>"Avg 142 FPS · 1% low 96 · 0.1% low 71".</summary>
    public static string FpsLine(GameSessionRecord r) =>
        string.Create(CultureInfo.InvariantCulture, $"Avg {r.AverageFps:0} FPS · 1% low {r.Low1Fps:0} · 0.1% low {r.Low01Fps:0}");

    /// <summary>"Min 58 · 12 stutters · 2h 05m".</summary>
    public static string DetailLine(GameSessionRecord r) =>
        string.Create(CultureInfo.InvariantCulture, $"Min {r.MinFps:0} · {r.Stutters} stutter{(r.Stutters == 1 ? "" : "s")} · {Duration(r.Seconds)}");

    /// <summary>"GPU avg 78% · max 92% · max temp 71°", or "" when the session has no hardware samples.</summary>
    public static string HardwareLine(GameSessionRecord r) => LoadLine("GPU", r.GpuAveragePercent, r.GpuPeakPercent, r.GpuPeakTemp);

    /// <summary>"CPU avg 41% · max 82% · max temp 71°", or "" when the session has no CPU numbers. The temperature only counts when it was measured.</summary>
    public static string CpuLine(GameSessionRecord r) => LoadLine("CPU", r.CpuAveragePercent, r.CpuPeakPercent, r.CpuPeakTemp);

    // The GPU and the CPU read the same way: average load, highest load, highest measured temperature.
    private static string LoadLine(string name, double? average, double? peak, double? temp)
    {
        var parts = new List<string>();
        if (average is { } a) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{name} avg {a:0}%"));
        if (peak is { } p) parts.Add(string.Create(CultureInfo.InvariantCulture, $"max {p:0}%"));
        if (temp is { } t) parts.Add(string.Create(CultureInfo.InvariantCulture, $"max temp {t:0}°"));
        return string.Join(" · ", parts);
    }

    // ---- the summary card shows each row as a few big numbers with a small caption (SummaryStat) ----

    /// <summary>One number of a summary row: the value ("142") and what it is ("avg FPS"). Warn marks a value worth a second look.</summary>
    public sealed record SummaryStat(string Value, string Caption, bool Warn = false);

    private static string N(double v) => v.ToString("0", CultureInfo.InvariantCulture);

    public static List<SummaryStat> SpeedStats(GameSessionRecord r) => new()
    {
        new(N(r.AverageFps), "avg FPS"), new(N(r.Low1Fps), "1% low"), new(N(r.Low01Fps), "0.1% low"),
    };

    public static List<SummaryStat> FrameStats(GameSessionRecord r) => new()
    {
        new(N(r.MinFps), "min FPS"), new(r.Stutters.ToString(CultureInfo.InvariantCulture), r.Stutters == 1 ? "stutter" : "stutters"), new(Duration(r.Seconds), "played"),
    };

    public static List<SummaryStat> GpuStats(GameSessionRecord r) => LoadStats(r.GpuAveragePercent, r.GpuPeakPercent, r.GpuPeakTemp);
    public static List<SummaryStat> CpuStats(GameSessionRecord r) => LoadStats(r.CpuAveragePercent, r.CpuPeakPercent, r.CpuPeakTemp);

    /// <summary>The temperature is flagged from this value up.</summary>
    public const double HotTemp = 85;

    private static List<SummaryStat> LoadStats(double? average, double? peak, double? temp)
    {
        var stats = new List<SummaryStat>();
        if (average is { } a) stats.Add(new(N(a) + "%", "avg load"));
        if (peak is { } p) stats.Add(new(N(p) + "%", "max load"));
        if (temp is { } t) stats.Add(new(N(t) + "°", "max temp", t >= HotTemp));
        return stats;
    }

    public static List<SummaryStat> PingStats(GameSessionRecord r)
    {
        var stats = new List<SummaryStat>();
        if (r.PingAverage is not { } average) return stats;
        stats.Add(new(N(average) + " ms", "avg ping"));
        stats.Add(new(N(r.PingWorst ?? 0) + " ms", "worst"));
        if (r.PingSpikes > 0) stats.Add(new(r.PingSpikes.ToString(CultureInfo.InvariantCulture), r.PingSpikes == 1 ? "spike" : "spikes"));
        if (r.PingLost > 0) stats.Add(new(r.PingLost.ToString(CultureInfo.InvariantCulture), "lost", Warn: true));
        return stats;
    }

    /// <summary>The new record first, the list capped at <see cref="HistoryCap"/>.</summary>
    public static List<GameSessionRecord> AddToHistory(IEnumerable<GameSessionRecord> existing, GameSessionRecord added)
    {
        var history = new[] { added }.Concat(existing).Take(HistoryCap).ToList();
        for (int i = TimelinesKept; i < history.Count; i++) history[i].Timeline = null;
        return history;
    }

    /// <summary>"Ping avg 23 · worst 112 · 2 spikes · 1 lost", or "" when the session has no ping numbers.</summary>
    public static string PingLine(GameSessionRecord r)
    {
        if (r.PingAverage is not { } average) return "";
        var parts = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"Ping avg {average:0}"),
            string.Create(CultureInfo.InvariantCulture, $"worst {r.PingWorst ?? 0:0}"),
        };
        if (r.PingSpikes > 0) parts.Add($"{r.PingSpikes} spike{(r.PingSpikes == 1 ? "" : "s")}");
        if (r.PingLost > 0) parts.Add($"{r.PingLost} lost");
        return string.Join(" · ", parts);
    }
}
