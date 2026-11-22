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
        var (line, fill) = HardwareChartMath.BuildAreaSeries(new[] { 0.0, 50.0, 150.0 }, 200.0, 100.0, 100.0);

        Assert.Equal(3, line.Count);
        Assert.Equal(5, fill.Count);
        Assert.Equal(new ChartPoint(0, 100.0), fill[0]);
        Assert.Equal(new ChartPoint(200.0, 100.0), fill[^1]);
        Assert.Equal(100.0, line[0].Y);
        Assert.Equal(3.0, line[2].Y, precision: 5);
    }

    [Fact]
    public void BuildMirroredSeries_MirrorsUpAndDownAroundMidline()
    {
        var result = HardwareChartMath.BuildMirroredSeries(new[] { 0.0, 50.0 }, new[] { 0.0, 50.0 }, 100.0, 152.0);

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
        var chip = HardwareChartMath.BuildChipSparkline(new[] { 0.0, 100.0 }, 100.0);
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
