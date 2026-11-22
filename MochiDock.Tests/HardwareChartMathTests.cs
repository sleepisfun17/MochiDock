using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class HardwareChartMathTests
{
    [Fact]
    public void PushBounded_CapsQueueAtMaxSamples()
    {
        var q = new Queue<double>();
        for (int i = 1; i <= 50; i++)
            HardwareChartMath.PushBounded(q, i);

        Assert.Equal(40, q.Count);
        Assert.Equal(11, q.Peek());
    }

    [Fact]
    public void BuildAreaSeries_ClosesPolygonAndClampsOutOfRangeValues()
    {
        var (line, fill) = HardwareChartMath.BuildAreaSeries(new[] { 0.0, 50.0, 150.0 }, 200.0, 100.0, 100.0, capacity: 3);

        Assert.Equal(3, line.Count);
        Assert.Equal(5, fill.Count);
        Assert.Equal(new ChartPoint(0, 100.0), fill[0]);
        Assert.Equal(new ChartPoint(200.0, 100.0), fill[^1]);
        Assert.Equal(100.0, line[0].Y);
        Assert.Equal(3.0, line[2].Y, precision: 5);
    }

    [Fact]
    public void BuildAreaSeries_FillingBufferGrowsInFromTheRightAtAFixedStep()
    {
        // Two samples of a 40-sample chart: the newest at the right edge, the other one slot (220 / 39) to its left,
        // not stretched across the whole width.
        var (line, fill) = HardwareChartMath.BuildAreaSeries(new[] { 10.0, 20.0 }, 220.0, 100.0, 100.0);
        double step = 220.0 / 39.0;

        Assert.Equal(220.0, line[1].X, precision: 9);
        Assert.Equal(220.0 - step, line[0].X, precision: 9);
        Assert.Equal(new ChartPoint(line[0].X, 100.0), fill[0]); // the area starts under the oldest sample
        Assert.Equal(new ChartPoint(220.0, 100.0), fill[^1]);
    }

    [Fact]
    public void FullBufferSpansTheWholeWidthLikeBefore()
    {
        var samples = Enumerable.Range(0, HardwareChartMath.DefaultHistoryCapacity).Select(i => (double)i).ToArray();
        var (line, _) = HardwareChartMath.BuildAreaSeries(samples, 220.0, 100.0, 100.0);
        Assert.Equal(0.0, line[0].X, precision: 9);
        Assert.Equal(220.0, line[^1].X, precision: 9);
        Assert.Equal(220.0 / 39.0, line[1].X - line[0].X, precision: 9);

        var chip = HardwareChartMath.BuildChipSparkline(samples, 100.0);
        Assert.Equal(0.75, chip[0].X, precision: 9);
        Assert.Equal(79.25, chip[^1].X, precision: 9);
    }

    [Fact]
    public void MirroredAndSparklineAlsoAnchorTheNewestSampleRight()
    {
        var mirrored = HardwareChartMath.BuildMirroredSeries(new[] { 1.0, 2.0, 3.0 }, new[] { 1.0, 2.0, 3.0 }, 220.0, 152.0);
        Assert.Equal(220.0, mirrored.UpLine[^1].X, precision: 9);
        Assert.Equal(220.0, mirrored.DownLine[^1].X, precision: 9);
        Assert.Equal(mirrored.UpLine[0].X, mirrored.UpFill[0].X, precision: 9);
        Assert.True(mirrored.UpLine[0].X > 200.0);

        var chip = HardwareChartMath.BuildChipSparkline(new[] { 0.0, 50.0 }, 100.0);
        Assert.Equal(79.25, chip[1].X, precision: 9);
        Assert.True(chip[0].X > 75.0);

        var single = HardwareChartMath.BuildAreaSeries(new[] { 50.0 }, 220.0, 100.0, 100.0);
        Assert.Equal(220.0, single.Line[0].X, precision: 9);
    }

    [Fact]
    public void BuildMirroredSeries_MirrorsUpAndDownAroundMidline()
    {
        var result = HardwareChartMath.BuildMirroredSeries(new[] { 0.0, 50.0 }, new[] { 0.0, 50.0 }, 100.0, 152.0, capacity: 2);

        Assert.True(result.NiceCeiling >= 50.0);
        Assert.Equal(2, result.UpLine.Count);
        Assert.Equal(2, result.DownLine.Count);
        Assert.Equal(76.0, result.UpLine[0].Y);
        Assert.Equal(76.0, result.DownLine[0].Y);
        Assert.True(result.UpLine[1].Y < 76.0);
        Assert.True(result.DownLine[1].Y > 76.0);
        Assert.Equal(76.0 - result.UpLine[1].Y, result.DownLine[1].Y - 76.0, precision: 5);
    }

    [Fact]
    public void BuildChipSparkline_AndFrameTimeAndCeiling_ProduceValidGeometry()
    {
        var chip = HardwareChartMath.BuildChipSparkline(new[] { 0.0, 100.0 }, 100.0, capacity: 2);
        Assert.Equal(2, chip.Count);
        Assert.Equal(3.25, chip[0].Y, precision: 5);
        Assert.Equal(0.75, chip[1].Y, precision: 5);

        var ft = HardwareChartMath.BuildFrameTimeSeries(new[] { 8.0, 16.0, 40.0 }, 100.0, 50.0);
        Assert.Equal(3, ft.Points.Count);
        Assert.Equal(40.0, ft.Ceiling);

        Assert.Equal(60f, HardwareChartMath.PickFpsCeiling(48));
        Assert.Equal(480f, HardwareChartMath.PickFpsCeiling(400));
    }
}
