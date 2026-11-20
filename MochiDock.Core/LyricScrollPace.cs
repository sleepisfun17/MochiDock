namespace MochiDock.Core;

/// <summary>How a lyric line that is wider than the capsule scrolls: a short pause, one pass to the end, then it holds.</summary>
/// <param name="StartPause">Seconds the start of the line stays still before scrolling.</param>
/// <param name="Speed">Scroll speed in DIPs per second.</param>
/// <param name="ScrollSeconds">Seconds the pass takes at <paramref name="Speed"/>.</param>
public readonly record struct LyricScrollPlan(double StartPause, double Speed, double ScrollSeconds);

/// <summary>
/// Paces a long lyric line to the time it is on screen, so its end is readable before the next line replaces it.
/// The user's marquee speed is the minimum: a line with time to spare scrolls at that speed and then holds at its end.
/// </summary>
public static class LyricScrollPace
{
    public const double MinStartPause = 0.25;
    public const double MaxStartPause = 0.8;
    /// <summary>Seconds the end of the line should stay readable before the next line.</summary>
    public const double EndHold = 0.6;
    /// <summary>Above this the text smears too much to read; a line too long for its time then ends slightly early.</summary>
    public const double MaxSpeed = 240;
    const double DefaultSpeed = 35;

    /// <param name="overflow">How far the text must move for its last character to show (DIPs).</param>
    /// <param name="lineSeconds">Seconds until the next lyric line.</param>
    /// <param name="minSpeed">The user's marquee speed (DIPs per second).</param>
    public static LyricScrollPlan Plan(double overflow, double lineSeconds, double minSpeed)
    {
        if (!double.IsFinite(overflow) || overflow <= 0) return new LyricScrollPlan(0, 0, 0);
        double floor = double.IsFinite(minSpeed) && minSpeed > 0 ? Math.Min(minSpeed, MaxSpeed) : DefaultSpeed;
        if (!double.IsFinite(lineSeconds) || lineSeconds <= 0)
            return new LyricScrollPlan(MaxStartPause, floor, overflow / floor);

        double startPause = Math.Clamp(lineSeconds * 0.15, MinStartPause, MaxStartPause);
        double available = lineSeconds - startPause - EndHold;
        double speed = available > 0 ? overflow / available : MaxSpeed;
        speed = Math.Clamp(speed, floor, MaxSpeed);
        return new LyricScrollPlan(startPause, speed, overflow / speed);
    }
}
