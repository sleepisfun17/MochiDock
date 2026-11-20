using MochiDock.Core;

public class CapsuleClockLayoutTests
{
    [Theory]
    [InlineData(35.2, 22, 49)]
    [InlineData(52, 22, 30)]
    [InlineData(35.2, 60, 40)]
    public void EqualEdgesCentresTheClock(double clock, double left, double right)
    {
        var l = CapsuleClockLayout.Compute(clock, left, right, CapsuleClockAlignment.EqualEdges, border: 2.4);
        Assert.Equal(l.LeftColumn, l.RightColumn, 6);
        Assert.Equal((l.PillWidth - 2.4) / 2, l.LeftColumn + l.ClockColumn / 2, 6);
        Assert.Equal(12, l.LeftOuter, 6);
        Assert.Equal(12, l.RightOuter, 6);
    }

    [Fact]
    public void EqualEdgesGivesTheNarrowerItemTheWiderGap()
    {
        var l = CapsuleClockLayout.Compute(35.2, 22, 49, CapsuleClockAlignment.EqualEdges);
        // Gaps are measured to the clock text, which sits half the safety margin (1 DIP) inside its column.
        Assert.Equal(12 + 27 + 1, l.LeftGap, 6);
        Assert.Equal(12 + 1, l.RightGap, 6);
    }

    [Theory]
    [InlineData(35.2, 22, 49)]
    [InlineData(35.2, 22, 70)]
    [InlineData(52, 22, 30)]
    [InlineData(35.2, 60, 40)]
    public void EvenSpacingMakesAllFourSpacesEqual(double clock, double left, double right)
    {
        var l = CapsuleClockLayout.Compute(clock, left, right, CapsuleClockAlignment.EvenSpacing, space: 12, border: 2.4);
        Assert.Equal(12, l.LeftOuter, 6);
        Assert.Equal(12, l.LeftGap, 6);
        Assert.Equal(12, l.RightGap, 6);
        Assert.Equal(12, l.RightOuter, 6);
        Assert.Equal(4 * 12 + left + clock + right + 2.4, l.PillWidth, 6);
        // The columns add up: rim + Mochi + gap + half the clock-column safety on the left, mirrored on the right.
        Assert.Equal(l.LeftOuter + left + l.LeftGap - CapsuleClockLayout.ClockSafety / 2, l.LeftColumn, 6);
        Assert.Equal(l.RightGap - CapsuleClockLayout.ClockSafety / 2 + right + l.RightOuter, l.RightColumn, 6);
    }

    [Theory]
    [InlineData(null, CapsuleClockAlignment.EqualEdges)]
    [InlineData("", CapsuleClockAlignment.EqualEdges)]
    [InlineData("EqualEdges", CapsuleClockAlignment.EqualEdges)]
    [InlineData("EvenSpacing", CapsuleClockAlignment.EvenSpacing)]
    [InlineData(" evenspacing ", CapsuleClockAlignment.EvenSpacing)]
    [InlineData("EqualGaps", CapsuleClockAlignment.EvenSpacing)] // the value an earlier build saved for this option
    [InlineData("Something else", CapsuleClockAlignment.EqualEdges)]
    public void SavedValueIsReadSafely(string? saved, CapsuleClockAlignment expected) =>
        Assert.Equal(expected, CapsuleClockLayout.Parse(saved));

    [Fact]
    public void ClockColumnAlwaysFitsTheMeasuredClock()
    {
        foreach (var alignment in new[] { CapsuleClockAlignment.EqualEdges, CapsuleClockAlignment.EvenSpacing })
            foreach (var w in new[] { 30.01, 34.5, 35.0, 48.9, 61.2 })
                Assert.True(CapsuleClockLayout.Compute(w, 22, 49, alignment).ClockColumn > w);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-5)]
    public void BadInputsDoNotProduceBadWidths(double bad)
    {
        foreach (var alignment in new[] { CapsuleClockAlignment.EqualEdges, CapsuleClockAlignment.EvenSpacing })
        {
            var l = CapsuleClockLayout.Compute(bad, bad, bad, alignment, bad, bad);
            Assert.True(double.IsFinite(l.PillWidth) && l.PillWidth >= 0);
            Assert.True(l.LeftColumn >= 0 && l.RightColumn >= 0);
            Assert.Equal(l.LeftColumn + l.ClockColumn + l.RightColumn, l.PillWidth, 6);
        }
    }
}
