using System.Text.Json.Serialization;

namespace MochiDock.Core;

/// <summary>One reading for the 24-hour Stats history. Temperatures are null unless measured (an estimate is never stored).</summary>
public readonly record struct HistorySample(DateTimeOffset At, double? CpuLoad, double? CpuTemp, double? GpuLoad, double? GpuTemp, double? RamPercent);

public enum HistoryMetric { CpuLoad, CpuTemp, GpuLoad, GpuTemp, RamPercent }

/// <summary>
/// One minute of the history: the average of each metric and the hottest temperature. <see cref="Minute"/> is minutes since the
/// Unix epoch (UTC). Each sum and count is kept so later samples in the same minute fold in exactly; a metric with no reading is null.
/// </summary>
public sealed class HistoryBucket
{
    [JsonPropertyName("m")] public long Minute { get; set; }
    [JsonPropertyName("c")] public double? CpuLoad { get; set; }
    [JsonPropertyName("ct")] public double? CpuTemp { get; set; }
    [JsonPropertyName("cx")] public double? CpuTempMax { get; set; }
    [JsonPropertyName("g")] public double? GpuLoad { get; set; }
    [JsonPropertyName("gt")] public double? GpuTemp { get; set; }
    [JsonPropertyName("gx")] public double? GpuTempMax { get; set; }
    [JsonPropertyName("r")] public double? RamPercent { get; set; }
    [JsonPropertyName("cn")] public int CpuLoadN { get; set; }
    [JsonPropertyName("ctn")] public int CpuTempN { get; set; }
    [JsonPropertyName("gn")] public int GpuLoadN { get; set; }
    [JsonPropertyName("gtn")] public int GpuTempN { get; set; }
    [JsonPropertyName("rn")] public int RamPercentN { get; set; }

    public DateTimeOffset Start => DateTimeOffset.FromUnixTimeSeconds(Minute * 60);

    public double? Value(HistoryMetric metric) => metric switch
    {
        HistoryMetric.CpuLoad => CpuLoad,
        HistoryMetric.CpuTemp => CpuTemp,
        HistoryMetric.GpuLoad => GpuLoad,
        HistoryMetric.GpuTemp => GpuTemp,
        _ => RamPercent,
    };

    /// <summary>The value the peak caption uses: the hottest reading for temperatures, the average otherwise.</summary>
    public double? PeakValue(HistoryMetric metric) => metric switch
    {
        HistoryMetric.CpuTemp => CpuTempMax,
        HistoryMetric.GpuTemp => GpuTempMax,
        _ => Value(metric),
    };

    internal void Fold(HistorySample s)
    {
        (CpuLoad, CpuLoadN) = Mean(CpuLoad, CpuLoadN, s.CpuLoad);
        (CpuTemp, CpuTempN) = Mean(CpuTemp, CpuTempN, s.CpuTemp);
        (GpuLoad, GpuLoadN) = Mean(GpuLoad, GpuLoadN, s.GpuLoad);
        (GpuTemp, GpuTempN) = Mean(GpuTemp, GpuTempN, s.GpuTemp);
        (RamPercent, RamPercentN) = Mean(RamPercent, RamPercentN, s.RamPercent);
        CpuTempMax = Max(CpuTempMax, s.CpuTemp);
        GpuTempMax = Max(GpuTempMax, s.GpuTemp);
    }

    private static (double?, int) Mean(double? mean, int n, double? value)
    {
        if (value is not { } v || !double.IsFinite(v)) return (mean, n);
        // Two decimals are plenty for a chart and keep the file small (about 1440 minutes a day).
        return mean is { } m ? (Math.Round(m + (v - m) / (n + 1), 2), n + 1) : (Math.Round(v, 2), 1);
    }

    private static double? Max(double? a, double? b) => b is not { } v || !double.IsFinite(v) ? a : a is { } x ? Math.Max(x, v) : v;
}

/// <summary>
/// The last 24 hours of Stats readings, one <see cref="HistoryBucket"/> per minute (at most 1440). Pure: the caller passes the time
/// and persists <see cref="Buckets"/>. Chart helpers draw it in the Stats charts' point space.
/// </summary>
public sealed class StatsHistory
{
    public static readonly TimeSpan Keep = TimeSpan.FromHours(24);
    /// <summary>Missing minutes longer than this (asleep, locked, MochiDock closed) break the line instead of joining across.</summary>
    public const int GapMinutes = 3;

    private readonly SortedList<long, HistoryBucket> _buckets = new();

    public StatsHistory(IEnumerable<HistoryBucket>? buckets = null)
    {
        foreach (var b in buckets ?? Enumerable.Empty<HistoryBucket>())
            if (b is not null) _buckets[b.Minute] = b;
    }

    public IReadOnlyList<HistoryBucket> Buckets => _buckets.Values.ToList();
    public int Count => _buckets.Count;

    public static long MinuteOf(DateTimeOffset at) => at.ToUnixTimeSeconds() / 60;

    public void Clear() => _buckets.Clear();

    public void Add(HistorySample sample)
    {
        long minute = MinuteOf(sample.At);
        if (!_buckets.TryGetValue(minute, out var bucket)) _buckets[minute] = bucket = new HistoryBucket { Minute = minute };
        bucket.Fold(sample);
    }

    /// <summary>Drops minutes older than <see cref="Keep"/> (and any from the future, after a clock change).</summary>
    public void Trim(DateTimeOffset now)
    {
        long oldest = MinuteOf(now - Keep), newest = MinuteOf(now) + 1;
        foreach (var key in _buckets.Keys.Where(k => k < oldest || k > newest).ToList()) _buckets.Remove(key);
    }

    /// <summary>
    /// The metric over the last <paramref name="span"/> as line segments in a <paramref name="width"/> x <paramref name="height"/> space:
    /// x runs from now - span (left) to now (right), y is 0..<paramref name="max"/> from the bottom (the same mapping as the 2-minute charts).
    /// A run of more than <see cref="GapMinutes"/> missing minutes starts a new segment; a single point is kept as a tiny segment.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> Series(HistoryMetric metric, DateTimeOffset now, TimeSpan span, double width, double height, double max)
    {
        var segments = new List<IReadOnlyList<(double, double)>>();
        long end = MinuteOf(now), start = MinuteOf(now - span);
        double minutes = Math.Max(1, end - start);
        List<(double, double)>? current = null;
        long? previous = null;
        foreach (var bucket in _buckets.Values)
        {
            if (bucket.Minute < start || bucket.Minute > end || bucket.Value(metric) is not { } value || !double.IsFinite(value)) continue;
            if (current is null || previous is { } p && bucket.Minute - p > GapMinutes)
            {
                if (current is { Count: > 0 }) segments.Add(current);
                current = new List<(double, double)>();
            }
            double x = (bucket.Minute - start) / minutes * width;
            double y = height - Math.Clamp(value / Math.Max(1, max), 0, 1) * (height - 3);
            current.Add((x, y));
            previous = bucket.Minute;
        }
        if (current is { Count: > 0 }) segments.Add(current);
        return segments;
    }

    /// <summary>The highest value of the metric in the span and the minute it happened, or null when there is none.</summary>
    public (double Value, DateTimeOffset At)? Peak(HistoryMetric metric, DateTimeOffset now, TimeSpan span)
    {
        long start = MinuteOf(now - span);
        (double, DateTimeOffset)? best = null;
        foreach (var bucket in _buckets.Values)
        {
            if (bucket.Minute < start || bucket.PeakValue(metric) is not { } v || !double.IsFinite(v)) continue;
            if (best is null || v > best.Value.Item1) best = (v, bucket.Start);
        }
        return best;
    }
}

/// <summary>
/// The sustained-heat alert: a measured temperature at or above the threshold for a whole hold time (by default 80 °C for 10 minutes).
/// Pure and clock-free. Fires once per hot spell; it re-arms after the temperature has been <see cref="RearmMargin"/> under the
/// threshold for <see cref="RearmAfter"/>. A gap in the readings longer than <see cref="MaxGap"/> restarts the count, and an
/// unmeasured reading (an estimate) never counts.
/// </summary>
public sealed class SustainedHeatPolicy
{
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan RearmAfter = TimeSpan.FromMinutes(2);
    public const double RearmMargin = 5;

    sealed class Spell { public DateTimeOffset? HotSince, CoolSince, Last; public bool Armed = true; }
    readonly Dictionary<string, Spell> _spells = new();

    /// <summary>Whether the metric is hot right now (at or above the threshold, measured, readings continuous).</summary>
    public bool IsHot(string metric) => _spells.TryGetValue(metric, out var s) && s.HotSince is not null;

    /// <summary>Whether the metric has been hot for at least <paramref name="holdFor"/> and still is (Mochi's hot face while it lasts).</summary>
    public bool IsSustained(string metric, TimeSpan holdFor, DateTimeOffset now) =>
        _spells.TryGetValue(metric, out var s) && s.HotSince is { } since && now - since >= holdFor;

    public void Reset() { foreach (var s in _spells.Values) { s.HotSince = null; s.CoolSince = null; s.Last = null; } }

    public bool Evaluate(string metric, double? measuredTemp, double threshold, TimeSpan holdFor, DateTimeOffset now)
    {
        if (!_spells.TryGetValue(metric, out var s)) _spells[metric] = s = new Spell();
        if (measuredTemp is not { } t || !double.IsFinite(t)) { s.HotSince = null; s.Last = null; return false; }
        if (s.Last is { } last && now - last > MaxGap) { s.HotSince = null; s.CoolSince = null; }
        s.Last = now;

        if (t < threshold)
        {
            s.HotSince = null;
            if (t <= threshold - RearmMargin) { s.CoolSince ??= now; if (now - s.CoolSince >= RearmAfter) s.Armed = true; }
            else s.CoolSince = null;
            return false;
        }
        s.CoolSince = null;
        s.HotSince ??= now;
        if (!s.Armed || now - s.HotSince < holdFor) return false;
        s.Armed = false;
        return true;
    }
}
