namespace MochiDock.Core;

/// <summary>How the resting capsule <c>[Mochi] clock [weather]</c> spreads its three items.</summary>
public enum CapsuleClockAlignment
{
    /// <summary>The clock is exactly centred; Mochi and the weather sit the same distance from the capsule's edges.</summary>
    EqualEdges,
    /// <summary>The same space before, between and after the three items (like CSS space-evenly); the clock may sit off centre.</summary>
    EvenSpacing,
}

/// <summary>Column widths and spaces for the resting capsule <c>[Mochi] clock [weather]</c>.</summary>
/// <param name="LeftColumn">Width of Mochi's column (Mochi is left-aligned <see cref="Edge"/> from the rim).</param>
/// <param name="ClockColumn">Width of the clock column: the measured clock plus <see cref="CapsuleClockLayout.ClockSafety"/>.</param>
/// <param name="RightColumn">Width of the weather column (the badge is right-aligned <see cref="Edge"/> from the rim).</param>
/// <param name="PillWidth">Total capsule width, border included.</param>
/// <param name="Alignment">The mode the layout was computed for.</param>
/// <param name="Edge">Space between each rim and its item.</param>
/// <param name="LeftOuter">Space from the inner left rim to Mochi.</param>
/// <param name="LeftGap">Space from Mochi to the clock text.</param>
/// <param name="RightGap">Space from the clock text to the weather badge.</param>
/// <param name="RightOuter">Space from the weather badge to the inner right rim.</param>
public readonly record struct CapsuleClockLayoutResult(double LeftColumn, double ClockColumn, double RightColumn, double PillWidth,
    CapsuleClockAlignment Alignment, double Edge, double LeftOuter, double LeftGap, double RightGap, double RightOuter);

/// <summary>
/// Lays out the resting capsule. In both modes Mochi sits <c>space</c> from the left rim and the weather <c>space</c> from the right rim;
/// <see cref="CapsuleClockAlignment.EqualEdges"/> widens the narrower side so the clock is exactly centred, and
/// <see cref="CapsuleClockAlignment.EvenSpacing"/> makes all four spaces equal.
/// </summary>
public static class CapsuleClockLayout
{
    public const double DefaultSpace = 12;
    /// <summary>Extra width on the clock column (half on each side) so sub-pixel text rendering never clips the last digit.</summary>
    public const double ClockSafety = 2;

    /// <summary>
    /// Reads the saved setting. "EvenSpacing" and the earlier "EqualGaps" mean <see cref="CapsuleClockAlignment.EvenSpacing"/>;
    /// anything else (including a missing value) is <see cref="CapsuleClockAlignment.EqualEdges"/>.
    /// </summary>
    public static CapsuleClockAlignment Parse(string? value)
    {
        string v = value?.Trim() ?? "";
        return v.Equals(nameof(CapsuleClockAlignment.EvenSpacing), StringComparison.OrdinalIgnoreCase) || v.Equals("EqualGaps", StringComparison.OrdinalIgnoreCase)
            ? CapsuleClockAlignment.EvenSpacing
            : CapsuleClockAlignment.EqualEdges;
    }

    public static CapsuleClockLayoutResult Compute(double clockWidth, double leftWidth, double rightWidth,
        CapsuleClockAlignment alignment = CapsuleClockAlignment.EqualEdges, double space = DefaultSpace, double border = 2.4)
    {
        clockWidth = Clean(clockWidth);
        leftWidth = Clean(leftWidth);
        rightWidth = Clean(rightWidth);
        space = Clean(space);
        border = Clean(border);

        // The exact measured width (not rounded up): rounding would add up to 1 DIP of slack on one side of the clock.
        double clockText = clockWidth;
        double clockColumn = clockText + ClockSafety;
        double half = ClockSafety / 2;

        double leftColumn, rightColumn;
        if (alignment == CapsuleClockAlignment.EvenSpacing)
        {
            // rim | s | Mochi | s | clock | s | weather | s | rim, with the clock column's safety margin taken out of the two inner spaces.
            leftColumn = Math.Max(0, space + leftWidth + space - half);
            rightColumn = Math.Max(0, space - half + rightWidth + space);
        }
        else
        {
            leftColumn = rightColumn = space + Math.Max(leftWidth, rightWidth) + space;
        }

        double pillWidth = leftColumn + clockColumn + rightColumn + border;
        double leftGap = leftColumn - space - leftWidth + half;
        double rightGap = rightColumn - space - rightWidth + half;
        return new CapsuleClockLayoutResult(leftColumn, clockColumn, rightColumn, pillWidth, alignment, space, space, leftGap, rightGap, space);
    }

    static double Clean(double value) => double.IsFinite(value) && value > 0 ? value : 0;
}
