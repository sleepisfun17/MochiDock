using MochiDock.Core;

public class LyricOffsetTests
{
    [Theory]
    [InlineData(0, 10.0, 10.0)]
    [InlineData(300, 10.0, 10.3)]
    [InlineData(-300, 10.0, 9.7)]
    [InlineData(-5000, 2.0, 0.0)]        // never goes below zero
    [InlineData(99999, 1.0, 6.0)]        // clamped to +5 s
    [InlineData(-99999, 20.0, 15.0)]     // clamped to -5 s
    public void ApplyShiftsPositionAndClamps(int offsetMs, double position, double expected) =>
        Assert.Equal(expected, LyricOffset.Apply(position, offsetMs), 6);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ApplyIgnoresNonFinitePositions(double position) => Assert.Equal(0, LyricOffset.Apply(position, 200));

    [Theory]
    [InlineData(0, "On time")]
    [InlineData(100, "Earlier 0.1s")]
    [InlineData(-1500, "Later 1.5s")]
    [InlineData(20000, "Earlier 5.0s")]
    public void LabelDescribesDirection(int offsetMs, string expected) => Assert.Equal(expected, LyricOffset.Label(offsetMs));

    [Fact]
    public void PositiveOffsetSelectsALineEarlierAtTheBoundary()
    {
        var timestamps = new[] { 0.0, 5.0, 10.0 };
        int Line(double position, int offset) => MediaPresentation.ActiveLine(timestamps, t => t, LyricOffset.Apply(position, offset));
        Assert.Equal(0, Line(4.8, 0));
        Assert.Equal(1, Line(4.8, 300));   // earlier: line 2 appears 0.3 s before its timestamp
        Assert.Equal(1, Line(5.1, 0));
        Assert.Equal(0, Line(5.1, -300));  // later: line 2 is held back
    }

    [Fact]
    public void ClampIsSymmetric()
    {
        Assert.Equal(5000, LyricOffset.Clamp(7000));
        Assert.Equal(-5000, LyricOffset.Clamp(-7000));
        Assert.Equal(120, LyricOffset.Clamp(120));
    }
}
