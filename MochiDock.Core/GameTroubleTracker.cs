using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>
/// Tells when a game is going badly, for Mochi's worried face: the frame rate fell well below what it has been (60 percent of the recent
/// median) or the ping is high, and it stays that way for a few seconds. It clears after a few good seconds, so the face does not flicker.
/// Only measured readings count (an FPS of 0 or a ping below 0 means no reading). Pure: the caller passes the time.
/// </summary>
public sealed class GameTroubleTracker
{
    public const double FpsDropRatio = 0.6;
    public const long BadPingMs = 150;
    /// <summary>A game that runs below this frame rate all the time is just slow, not in trouble: the drop rule is off for it.</summary>
    public const int MinBaselineFps = 30;
    public const int MinSamples = 30;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan EnterAfter = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ClearAfter = TimeSpan.FromSeconds(3);

    private readonly Queue<(DateTime Time, int Fps)> _samples = new();
    private DateTime? _badSince;
    private DateTime? _okSince;
    private bool _active;

    public bool Active => _active;

    /// <summary>Forgets everything (a game started or ended).</summary>
    public void Reset()
    {
        _samples.Clear();
        _badSince = _okSince = null;
        _active = false;
    }

    /// <summary>Feeds one reading and returns whether the game is in trouble now.</summary>
    public bool Update(DateTime now, int fps, long pingMs)
    {
        while (_samples.Count > 0 && now - _samples.Peek().Time > Window) _samples.Dequeue();

        bool fpsBad = false;
        if (fps > 0)
        {
            if (_samples.Count >= MinSamples)
            {
                double baseline = Median();
                fpsBad = baseline >= MinBaselineFps && fps < baseline * FpsDropRatio;
            }
            // A drop is not added to the baseline, so a long drop does not drag its own yardstick down.
            if (!fpsBad) _samples.Enqueue((now, fps));
        }
        bool bad = fpsBad || pingMs >= BadPingMs;

        if (bad)
        {
            _okSince = null;
            _badSince ??= now;
            if (!_active && now - _badSince >= EnterAfter) _active = true;
        }
        else
        {
            _badSince = null;
            if (_active)
            {
                _okSince ??= now;
                if (now - _okSince >= ClearAfter) { _active = false; _okSince = null; }
            }
        }
        return _active;
    }

    private double Median()
    {
        var sorted = _samples.Select(s => s.Fps).OrderBy(f => f).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
