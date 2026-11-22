using MochiDock.Core;

public class PowerChipTests
{
    static PowerReading Reading(double watts, PowerSource source) => new(watts, source, DateTimeOffset.UnixEpoch, "");

    [Fact]
    public void TextMarksEstimatesAndShowsADashWithoutAReading()
    {
        Assert.Equal("~142 W", PowerChip.Text(Reading(142.4, PowerSource.Estimated)));
        Assert.Equal("38.5 W", PowerChip.Text(Reading(38.46, PowerSource.Measured)));
        Assert.Equal("-", PowerChip.Text(null));
    }

    [Fact]
    public void SparklineSpansTheChipAndItsOwnRange()
    {
        var points = PowerChip.SparkPoints(new[] { 100.0, 150.0, 200.0 }, capacity: 3);
        Assert.Equal(3, points.Count);
        Assert.Equal(0.75, points[0].X, 3);
        Assert.Equal(79.25, points[^1].X, 3);
        Assert.Equal(3.25, points[0].Y, 3);   // lowest sample at the bottom
        Assert.Equal(0.75, points[^1].Y, 3);  // highest at the top
    }

    [Fact]
    public void AFillingHistoryGrowsInFromTheRight()
    {
        // Two samples of a 60-sample history: the newest at the right edge, the other one slot (78.5 / 59) to its left.
        var points = PowerChip.SparkPoints(new[] { 100.0, 200.0 }, capacity: 60);
        Assert.Equal(79.25, points[1].X, 3);
        Assert.Equal(79.25 - 78.5 / 59, points[0].X, 3);
    }

    [Fact]
    public void AFlatOrShortHistoryStaysSensible()
    {
        Assert.Empty(PowerChip.SparkPoints(new[] { 90.0 }));
        Assert.All(PowerChip.SparkPoints(new[] { 90.0, 90.1, 90.0 }), p => Assert.Equal(2.0, p.Y, 3));
    }
}
