using System;
using System.Collections.Generic;

namespace MochiDock.Core;

/// <summary>
/// Counts clicks on Mochi. Eight clicks within five seconds make him dizzy; he stays dizzy for three seconds after the last click and
/// at most six seconds in all, then the count starts again from zero. Clicks spread out over time never trigger it. Pure: the caller
/// passes the time.
/// </summary>
public sealed class PettingTracker
{
    public const int ClicksToDizzy = 8;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DizzyHold = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DizzyMax = TimeSpan.FromSeconds(6);

    private readonly Queue<DateTime> _clicks = new();
    private DateTime? _since;
    private DateTime? _until;

    /// <summary>When the current dizziness ends if nobody clicks again, or null when Mochi is not dizzy.</summary>
    public DateTime? DizzyUntil => _until;

    public bool IsDizzy(DateTime now) => _since != null && _until is { } until && now < until;

    /// <summary>Records a click and returns whether Mochi is dizzy now (including because of this click).</summary>
    public bool Register(DateTime now)
    {
        if (_since is { } since)
        {
            if (now >= _until || now - since >= DizzyMax)
            {
                // The dizziness is over (it ran out, or the click spree went on for the whole maximum): start counting afresh.
                _since = null;
                _until = null;
                _clicks.Clear();
            }
            else
            {
                // Still dizzy: another click keeps him dizzy a little longer, but never past the maximum.
                var extended = now + DizzyHold;
                var cap = since + DizzyMax;
                _until = extended < cap ? extended : cap;
                return true;
            }
        }

        while (_clicks.Count > 0 && now - _clicks.Peek() > Window) _clicks.Dequeue();
        _clicks.Enqueue(now);
        if (_clicks.Count < ClicksToDizzy) return false;

        _since = now;
        _until = now + DizzyHold;
        _clicks.Clear();
        return true;
    }
}
