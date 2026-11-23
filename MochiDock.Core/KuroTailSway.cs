using System;

namespace MochiDock.Core;

/// <summary>When Kuro sways his tail: now and then (6 to 12 seconds apart) while he is relaxed or happy. Pure so the rule can be tested.</summary>
public static class KuroTailSway
{
    public const double MinSeconds = 6;
    public const double MaxSeconds = 12;

    /// <summary>The wait until the next sway; <paramref name="unit"/> is a random number from 0 to 1.</summary>
    public static TimeSpan NextDelay(double unit)
    {
        double u = double.IsFinite(unit) ? Math.Clamp(unit, 0, 1) : 0.5;
        return TimeSpan.FromSeconds(MinSeconds + u * (MaxSeconds - MinSeconds));
    }

    /// <summary>
    /// He sways while relaxed or happy. Not while sleeping, not with the still faces (Focused, Worried, BreakTime, Waking, Bored, Studying,
    /// Meeting) and not when unwell or sad (Dizzy, Drained, Defeat, Freezing, Hot, Angry); the activity faces stay quiet too.
    /// </summary>
    public static bool CanSway(MascotMood mood) =>
        mood is MascotMood.Idle or MascotMood.Waiting or MascotMood.Happy or MascotMood.Music or MascotMood.Coffee
            or MascotMood.Proud or MascotMood.Birthday or MascotMood.Cheering or MascotMood.Excited;
}
