using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MochiDock.Core;

/// <summary>How a finished session compares with the usual sessions of the same game.</summary>
public sealed record GameTrendResult(int Sessions, double AverageFpsDelta, double Low1Delta, double? PingDelta);

/// <summary>Compares a session with the median of the earlier sessions of the same game.</summary>
public static class GameTrend
{
    public const int MinSessions = 3;
    public const int MaxSessions = 20;

    /// <summary>Game names match when they are equal after trimming, ignoring case.</summary>
    public static bool SameGame(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The difference to the median average FPS, 1% low and ping of up to <see cref="MaxSessions"/> earlier sessions of the same
    /// game (<paramref name="earlier"/> is newest first). Null when fewer than <see cref="MinSessions"/> earlier sessions exist.
    /// The ping difference needs at least <see cref="MinSessions"/> earlier sessions with ping numbers.
    /// </summary>
    public static GameTrendResult? Compare(GameSessionRecord current, IEnumerable<GameSessionRecord> earlier)
    {
        var usual = earlier.Where(r => !ReferenceEquals(r, current) && SameGame(r.Game, current.Game)).Take(MaxSessions).ToList();
        if (usual.Count < MinSessions) return null;

        double? ping = null;
        var pings = usual.Where(r => r.PingAverage.HasValue).Select(r => r.PingAverage!.Value).ToList();
        if (current.PingAverage.HasValue && pings.Count >= MinSessions) ping = current.PingAverage.Value - Median(pings);

        return new GameTrendResult(usual.Count,
            current.AverageFps - Median(usual.Select(r => r.AverageFps).ToList()),
            current.Low1Fps - Median(usual.Select(r => r.Low1Fps).ToList()),
            ping);
    }

    public static double Median(IList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        return n == 0 ? 0 : n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    /// <summary>"vs your usual (8 sessions): avg +6 FPS, 1% low -3, ping +12 ms", or "about your usual (8 sessions)" when nothing moved noticeably.</summary>
    public static string Line(GameTrendResult trend)
    {
        string Signed(double v) => (v >= 0 ? "+" : "-") + Math.Abs(v).ToString("0", CultureInfo.InvariantCulture);
        bool fpsMoved = Math.Abs(trend.AverageFpsDelta) >= 1 || Math.Abs(trend.Low1Delta) >= 1;
        bool pingMoved = trend.PingDelta is { } p && Math.Abs(p) >= 3;
        if (!fpsMoved && !pingMoved) return $"About your usual ({trend.Sessions} sessions)";

        var parts = new List<string>();
        if (fpsMoved)
        {
            parts.Add($"avg {Signed(trend.AverageFpsDelta)} FPS");
            parts.Add($"1% low {Signed(trend.Low1Delta)}");
        }
        if (pingMoved) parts.Add($"ping {Signed(trend.PingDelta!.Value)} ms");
        return $"vs your usual ({trend.Sessions} sessions): {string.Join(", ", parts)}";
    }
}
