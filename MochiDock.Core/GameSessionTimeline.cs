using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>One point of a session graph: the bucket's average FPS, its slowest frame and its worst ping.</summary>
public sealed class GameTimelinePoint
{
    public double Fps { get; set; }
    public double WorstFrameMs { get; set; }
    /// <summary>The worst ping measured in this bucket, or null when none was measured.</summary>
    public double? PingMs { get; set; }
    /// <summary>True when a ping was sent in this bucket but no answer came back.</summary>
    public bool PingLost { get; set; }
}

/// <summary>
/// A session's graph data in constant memory: at most <see cref="MaxBuckets"/> buckets of equal width. The width starts at
/// <see cref="StartWidthSeconds"/>; when the buckets are full, neighbours are merged and the width doubles. Time is the sum of
/// the frame times, the same clock as <see cref="GameSessionStats"/>.
/// </summary>
public sealed class GameSessionTimeline
{
    public const int MaxBuckets = 360;
    public const double StartWidthSeconds = 2;
    private const double PauseMs = GameSessionStats.PauseMs;

    private struct Bucket
    {
        public long Frames;
        public double TimeMs, WorstMs, WorstPing;
        public bool HasPing, Lost;

        public static Bucket Merge(Bucket a, Bucket b) => new()
        {
            Frames = a.Frames + b.Frames, TimeMs = a.TimeMs + b.TimeMs, WorstMs = Math.Max(a.WorstMs, b.WorstMs),
            WorstPing = Math.Max(a.HasPing ? a.WorstPing : 0, b.HasPing ? b.WorstPing : 0), HasPing = a.HasPing || b.HasPing, Lost = a.Lost || b.Lost,
        };
    }

    private readonly List<Bucket> _buckets = new() { new Bucket() };
    private double _widthMs = StartWidthSeconds * 1000;

    /// <summary>The seconds one bucket covers now.</summary>
    public double BucketSeconds => _widthMs / 1000.0;

    public int BucketCount => _buckets.Count;

    /// <summary>Adds one frame time. Pauses and values that are not frame times are ignored.</summary>
    public void AddFrame(double frameMs)
    {
        if (!double.IsFinite(frameMs) || frameMs <= 0 || frameMs >= PauseMs) return;
        var current = _buckets[^1];
        current.Frames++;
        current.TimeMs += frameMs;
        if (frameMs > current.WorstMs) current.WorstMs = frameMs;
        _buckets[^1] = current;
        if (current.TimeMs >= _widthMs) StartNextBucket();
    }

    /// <summary>Adds a ping result to the bucket being filled: the round trip in ms, or null when no answer came back.</summary>
    public void AddPing(double? pingMs)
    {
        var current = _buckets[^1];
        if (pingMs is { } ms && double.IsFinite(ms) && ms >= 0)
        {
            current.WorstPing = current.HasPing ? Math.Max(current.WorstPing, ms) : ms;
            current.HasPing = true;
        }
        else current.Lost = true;
        _buckets[^1] = current;
    }

    private void StartNextBucket()
    {
        if (_buckets.Count >= MaxBuckets)
        {
            var merged = new List<Bucket>(MaxBuckets / 2 + 1);
            for (int i = 0; i + 1 < _buckets.Count; i += 2) merged.Add(Bucket.Merge(_buckets[i], _buckets[i + 1]));
            if (_buckets.Count % 2 == 1) merged.Add(_buckets[^1]);
            _buckets.Clear();
            _buckets.AddRange(merged);
            _widthMs *= 2;
        }
        _buckets.Add(new Bucket());
    }

    /// <summary>The points so far, oldest first; the bucket still being filled is included when it has frames.</summary>
    public List<GameTimelinePoint> Points() => _buckets
        .Where(b => b.Frames > 0 && b.TimeMs > 0)
        .Select(b => new GameTimelinePoint
        {
            Fps = b.Frames / (b.TimeMs / 1000.0),
            WorstFrameMs = b.WorstMs,
            PingMs = b.HasPing ? b.WorstPing : null,
            PingLost = b.Lost,
        })
        .ToList();
}

/// <summary>Ping numbers of one session. Needs at least <see cref="MinSamples"/> answered pings.</summary>
public sealed record PingResult(double Average, double Worst, double Jitter, int Spikes, int Lost);

/// <summary>Collects ping results: average, worst, jitter (mean change between neighbours), spikes and lost pings.</summary>
public sealed class PingStats
{
    public const int MinSamples = 5;
    private const int MaxKept = 20000;
    private readonly List<double> _pings = new();
    private double _sum, _worst, _jitterSum;
    private int _lost, _answered;
    private double _previous = -1;

    public void Add(double? pingMs)
    {
        if (pingMs is not { } ms || !double.IsFinite(ms) || ms < 0) { _lost++; return; }
        _answered++;
        _sum += ms;
        if (ms > _worst) _worst = ms;
        if (_previous >= 0) _jitterSum += Math.Abs(ms - _previous);
        _previous = ms;
        if (_pings.Count < MaxKept) _pings.Add(ms);
    }

    public PingResult? Result()
    {
        if (_answered < MinSamples) return null;
        var sorted = _pings.OrderBy(p => p).ToList();
        double median = sorted[sorted.Count / 2];
        // A spike is far above the typical ping in both relative and absolute terms, so a steady 5 ms line never spikes.
        int spikes = _pings.Count(p => p > 2 * median && p > median + 30);
        return new PingResult(_sum / _answered, _worst, _answered > 1 ? _jitterSum / (_answered - 1) : 0, spikes, _lost);
    }
}

/// <summary>The drawn shape of a session graph, in a <c>width</c> x <c>height</c> box (y grows downward).</summary>
public sealed record GameGraphShape(
    IReadOnlyList<(double X, double Y)> Fps, IReadOnlyList<(double X, double Y)> Ping, IReadOnlyList<double> StutterX, double MaxFps, double MaxPing);

/// <summary>Turns timeline points into coordinates: FPS and ping each scaled to the box, stutter positions marked.</summary>
public static class GameGraphLayout
{
    /// <summary>
    /// FPS runs from 0 to its maximum, ping from 0 to the larger of its worst value and 50 ms (so small pings look small).
    /// A point is a stutter when its slowest frame is more than twice the median frame time of the session and slower than 20 ms (<see cref="GameSessionStats.IsStutter"/>).
    /// </summary>
    public static GameGraphShape? Layout(IReadOnlyList<GameTimelinePoint>? points, double width, double height)
    {
        if (points == null || points.Count < 2 || width <= 0 || height <= 0) return null;
        double maxFps = Math.Max(1, points.Max(p => p.Fps));
        double maxPing = Math.Max(50, points.Where(p => p.PingMs.HasValue).Select(p => p.PingMs!.Value).DefaultIfEmpty(0).Max());
        double step = width / (points.Count - 1);

        var frameTimes = points.Where(p => p.Fps > 0).Select(p => 1000.0 / p.Fps).OrderBy(v => v).ToList();
        double median = frameTimes.Count == 0 ? 0 : frameTimes[frameTimes.Count / 2];

        var fps = new List<(double, double)>();
        var ping = new List<(double, double)>();
        var stutters = new List<double>();
        for (int i = 0; i < points.Count; i++)
        {
            double x = i * step;
            fps.Add((x, height - points[i].Fps / maxFps * height));
            if (points[i].PingMs is { } p) ping.Add((x, height - Math.Min(p, maxPing) / maxPing * height));
            if (median > 0 && GameSessionStats.IsStutter(points[i].WorstFrameMs, median)) stutters.Add(x);
        }
        return new GameGraphShape(fps, ping, stutters, maxFps, maxPing);
    }
}
