using MochiDock.Core;

public class CpuTelemetryTests
{
    [Fact] public void UsesOneIntervalAndExcludesIdleFromKernel()
    {
        var sampler = new CpuTimeSampler();
        Assert.Null(sampler.Sample(new(100, 200, 100)));
        var reading = sampler.Sample(new(160, 300, 200))!;
        Assert.Equal(20, reading.SystemPercent);
        Assert.Equal(50, reading.UserPercent);
        Assert.Equal(30, reading.IdlePercent);
        Assert.Equal(70, reading.UsagePercent);
        Assert.Equal(100, reading.SystemPercent + reading.UserPercent + reading.IdlePercent, 8);
    }
    [Fact] public void RepeatedSamplesCounterResetsAndResumeNeedABaseline()
    {
        var sampler = new CpuTimeSampler();
        sampler.Sample(new(100, 200, 100));
        Assert.Null(sampler.Sample(new(100, 200, 100)));
        Assert.Null(sampler.Sample(new(1, 2, 1)));
        Assert.NotNull(sampler.Sample(new(11, 22, 11)));
        sampler.Reset();
        Assert.Null(sampler.Sample(new(21, 42, 21)));
    }
    [Fact] public void PreservesZeroAndChangesWithTheNextInterval()
    {
        var sampler = new CpuTimeSampler(); sampler.Sample(new(0, 0, 0));
        var idle = sampler.Sample(new(100, 100, 0))!;
        Assert.Equal(0, idle.SystemPercent); Assert.Equal(0, idle.UserPercent); Assert.Equal(100, idle.IdlePercent);
        var loaded = sampler.Sample(new(100, 130, 70))!;
        Assert.Equal(30, loaded.SystemPercent); Assert.Equal(70, loaded.UserPercent); Assert.Equal(0, loaded.IdlePercent);
    }
    [Fact] public void RejectsInvalidKernelDeltasAndDoesNotOverflowLargeTotals()
    {
        var sampler = new CpuTimeSampler(); sampler.Sample(new(0, 0, 0));
        Assert.Null(sampler.Sample(new(100, 50, 0)));
        sampler.Reset(); sampler.Sample(new(0, 0, 0));
        var large = sampler.Sample(new(0, ulong.MaxValue, ulong.MaxValue))!;
        Assert.Equal(50, large.SystemPercent); Assert.Equal(50, large.UserPercent);
    }
    [Theory]
    [InlineData(3600, 65.5, 2.358)]
    [InlineData(3600, 125, 4.5)]
    [InlineData(3600, 0, 0)]
    public void FrequencyUsesMeasuredPerformanceAndAllowsBoost(double mhz, double percent, double expected)
        => Assert.Equal(expected, CpuClock.Ghz(mhz, percent)!.Value, 8);
    [Fact] public void MissingOrInvalidClockInputsStayUnavailable()
    {
        Assert.Null(CpuClock.Ghz(0, 100)); Assert.Null(CpuClock.Ghz(3600, -1));
        Assert.Null(CpuClock.Ghz(double.NaN, 100)); Assert.Null(CpuClock.Ghz(3600, double.PositiveInfinity));
    }
    [Theory]
    [InlineData(2.5, null, 0, 4.5)]
    [InlineData(2.5, 3.8, 0, 4.5)]
    [InlineData(2.5, 4.6, 4.5, 4.8)]
    [InlineData(3.6, 5.2, 5.5, 5.5)]
    public void BoostCeilingProvidesHeadroomAndExpandsWithObservedPeak(double baseGhz, double? currentGhz, double prevCeiling, double expectedMin)
    {
        double ceiling = CpuClock.CeilingGhz(baseGhz, currentGhz, prevCeiling);
        Assert.True(ceiling >= expectedMin);
        if (currentGhz.HasValue)
        {
            Assert.True(ceiling >= currentGhz.Value);
        }
    }
}
