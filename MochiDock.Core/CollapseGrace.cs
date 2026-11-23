namespace MochiDock.Core;

public enum CollapseGraceDecision { Wait, Collapse }

/// <summary>
/// When the open dock shrinks out from under a still cursor (a notification dismissed, all cleared), the pointer "leaves" without the user
/// moving. Closing on that leave felt like an instant close right after a click. Instead the dock waits until the user clearly moves away,
/// or until <see cref="MaxWait"/> passes. A normal mouse-out still uses the short leave delay; this rule is only for shrink-caused leaves.
/// </summary>
public static class CollapseGrace
{
    /// <summary>How far the cursor must have moved since the leave (DIP) before moving away counts.</summary>
    public const double MoveAwayDip = 40;
    /// <summary>How far outside the dock the cursor must be (DIP) for moving away to count.</summary>
    public const double OutsideDip = 24;
    /// <summary>The longest the dock stays open after a shrink-caused leave with the cursor outside.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(4);
    /// <summary>A leave this soon after the dock got shorter counts as caused by the shrink.</summary>
    public static readonly TimeSpan ShrinkWindow = TimeSpan.FromMilliseconds(500);

    public static bool IsShrinkLeave(TimeSpan sinceShrink) => sinceShrink >= TimeSpan.Zero && sinceShrink < ShrinkWindow;

    public static CollapseGraceDecision Decide(TimeSpan sinceLeave, double cursorMovedDip, double distanceFromDockDip)
    {
        if (sinceLeave >= MaxWait) return CollapseGraceDecision.Collapse;
        if (cursorMovedDip > MoveAwayDip && distanceFromDockDip > OutsideDip) return CollapseGraceDecision.Collapse;
        return CollapseGraceDecision.Wait;
    }
}
