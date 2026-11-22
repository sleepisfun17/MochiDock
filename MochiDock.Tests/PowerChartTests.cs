using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class PowerChartTests
{
    [Theory]
    [InlineData(20, 25)]
    [InlineData(45, 50)]
    [InlineData(142, 200)]    // 142 * 1.1 = 156 → 200: headroom above a steady draw
    [InlineData(180, 200)]
    [InlineData(185, 250)]
    [InlineData(1900, 2500)]  // past the table: next 500 W step
    public void CeilingIsARoundWattageWithHeadroomFromZero(double peak, double expected) =>
        Assert.Equal(expected, PowerChart.NiceCeiling(peak));

    [Fact]
    public void FewerThanTwoSamplesDrawNothing()
    {
        Assert.Null(PowerChart.Build(new PowerSample[0], 60, 200, 50));
        Assert.Null(PowerChart.Build(new[] { new PowerSample(100, 30, 40) }, 60, 200, 50));
    }

    [Fact]
    public void DesktopSamplesStackOtherCpuAndGpuUpToTheTotal()
    {
        var samples = new[] { new PowerSample(100, 30, 35), new PowerSample(150, 40, 75) };
        var g = PowerChart.Build(samples, 2, 200, 52)!;

        Assert.True(g.HasBreakdown);
        Assert.Equal(200, g.Ceiling);
        Assert.Equal("200 W", g.CeilingText);
        double Y(double w) => 52 - w / 200 * 50;
        // Each band is its top edge left to right, then its bottom edge right to left.
        Assert.Equal(new ChartPoint(0, Y(35)), g.OtherArea[0]);            // 100 - 30 - 35 = 35 W other
        Assert.Equal(new ChartPoint(0, 52), g.OtherArea[^1]);              // ...down to the axis
        Assert.Equal(new ChartPoint(0, Y(65)), g.CpuArea[0]);              // other + CPU
        Assert.Equal(new ChartPoint(0, Y(100)), g.GpuArea[0]);             // GPU tops out at the total
        Assert.Equal(g.TotalLine[1], g.GpuArea[1]);
        Assert.Equal(125, g.Average);
        Assert.Equal(150, g.Peak);
        Assert.Equal(Y(125), g.AverageY, 9);
        Assert.Equal(g.TotalLine[1], g.PeakPoint);
    }

    [Fact]
    public void ABatteryRateIsOneAreaUnderTheTotal()
    {
        var g = PowerChart.Build(new[] { new PowerSample(12, null, null), new PowerSample(18, null, null) }, 2, 100, 40)!;
        Assert.False(g.HasBreakdown);
        Assert.Empty(g.CpuArea);
        Assert.Empty(g.GpuArea);
        Assert.Equal(g.TotalLine[0], g.OtherArea[0]);
        Assert.Equal(4, g.OtherArea.Count);
    }

    [Fact]
    public void PartsLargerThanTheTotalAreClampedSoBandsNeverCross()
    {
        var g = PowerChart.Build(new[] { new PowerSample(50, 40, 40), new PowerSample(50, 40, 40) }, 2, 100, 40)!;
        // CPU 40 fits, GPU is cut to the 10 W left, other is 0: the bands still meet the total exactly.
        Assert.Equal(40, g.OtherArea[0].Y, 9);
        Assert.Equal(g.TotalLine[0].Y, g.GpuArea[0].Y, 9);
        Assert.True(g.CpuArea[0].Y >= g.GpuArea[0].Y);
    }

    [Fact]
    public void AFillingHistoryGrowsInFromTheRight()
    {
        var g = PowerChart.Build(new[] { new PowerSample(80, null, null), new PowerSample(90, null, null) }, 60, 236, 40)!;
        Assert.Equal(236, g.TotalLine[1].X, 9);
        Assert.Equal(236 - 236.0 / 59, g.TotalLine[0].X, 9);
    }

    [Fact]
    public void HistoryKeepsThePartsWithEachTotal()
    {
        var h = new PowerHistory(2);
        h.Add(100, 30, 40);
        h.Add(20);                       // a battery rate: no parts
        h.Add(120, 50, double.NaN);      // an unreadable part is dropped, the sample stays
        var points = h.Points();
        Assert.Equal(new PowerSample(20, null, null), points[0]);
        Assert.Equal(new PowerSample(120, 50, null), points[1]);
        Assert.Equal(new[] { 20.0, 120 }, h.Samples());
    }

    [Fact]
    public void TheDesktopEstimateCarriesItsMeasuredParts()
    {
        var reading = PowerEstimator.Estimate(45, 120, 5, DateTimeOffset.UnixEpoch)!;
        Assert.Equal(45, reading.CpuW);
        Assert.Equal(120, reading.GpuW);
        Assert.Equal(45 + 120 + 5 + PowerEstimator.DefaultAllowanceWatts, reading.Watts);
    }
}
