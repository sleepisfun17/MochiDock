using System;

namespace MochiDock.Core;

/// <summary>When Mochi winks: now and then (3 to 6 minutes apart) while he is only idling. Pure so the spacing can be tested.</summary>
public static class MochiWink
{
    public const double MinMinutes = 3;
    public const double MaxMinutes = 6;

    /// <summary>The wait until the next wink; <paramref name="unit"/> is a random number from 0 to 1.</summary>
    public static TimeSpan NextDelay(double unit)
    {
        double u = double.IsFinite(unit) ? Math.Clamp(unit, 0, 1) : 0.5;
        return TimeSpan.FromMinutes(MinMinutes + u * (MaxMinutes - MinMinutes));
    }

    /// <summary>He only winks while idling (and waiting), never during any other face.</summary>
    public static bool CanWink(MascotMood mood) => mood is MascotMood.Idle or MascotMood.Waiting;
}
