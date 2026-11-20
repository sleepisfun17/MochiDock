using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class PowerUsageTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EstimateSumsMeasuredPartsAndAllowance()
    {
        var r = PowerEstimator.Estimate(45, 98, 6, Now);
        Assert.NotNull(r);
        Assert.Equal(45 + 98 + 6 + PowerEstimator.DefaultAllowanceWatts, r!.Watts);
        Assert.Equal(PowerSource.Estimated, r.Source);
        Assert.Contains("CPU 45", r.Detail);
        Assert.Contains("GPU 98", r.Detail);
    }

    [Fact]
    public void EstimateWorksWithOnlyOnePartMeasured()
    {
        Assert.Equal(50 + 35, PowerEstimator.Estimate(null, 50, null, Now)!.Watts);
        Assert.Equal(20 + 35, PowerEstimator.Estimate(20, null, null, Now)!.Watts);
    }

    [Fact]
    public void EstimateIsUnavailableWithoutCpuOrGpu()
    {
        Assert.Null(PowerEstimator.Estimate(null, null, 5, Now));
        Assert.Null(PowerEstimator.Estimate(0, double.NaN, null, Now));
    }

    [Fact]
    public void BatteryRateUsesAbsoluteWatts()
    {
        Assert.Equal(14.2, BatteryRate.ToWatts(-14200));
        Assert.Equal(30.0, BatteryRate.ToWatts(30000));
        var discharging = BatteryRate.Reading(-14200, Now)!;
        Assert.Equal(PowerSource.Measured, discharging.Source);
        Assert.Contains("discharge", discharging.Detail);
        Assert.Contains("charge rate", BatteryRate.Reading(25000, Now)!.Detail);
    }

    [Fact]
    public void BatteryRateRejectsUnknownZeroAndAbsurd()
    {
        Assert.Null(BatteryRate.ToWatts(BatteryRate.UnknownRate));
        Assert.Null(BatteryRate.ToWatts(0));
        Assert.Null(BatteryRate.ToWatts(-900_000));
    }

    [Fact]
    public void HistoryKeepsOnlyTheNewestSamplesOldestFirst()
    {
        var h = new PowerHistory(3);
        foreach (var v in new[] { 1.0, 2, 3, 4, 5 }) h.Add(v);
        Assert.Equal(3, h.Count);
        Assert.Equal(new[] { 3.0, 4, 5 }, h.Samples());
        Assert.Equal(4, h.Average);
        Assert.Equal(5, h.Peak);
        h.Add(double.NaN);
        h.Add(-1);
        Assert.Equal(3, h.Count);
        h.Clear();
        Assert.Null(h.Average);
    }

    [Fact]
    public void SensorPickerReadsCpuPackageAndMemoryPower()
    {
        var sensors = new List<RawSensor>
        {
            new("Cpu", "Intel Core", "Power", "CPU Package", 45.5f),
            new("Cpu", "Intel Core", "Power", "CPU Cores", 38f),
            new("Cpu", "Intel Core", "Power", "CPU Memory", 4.2f),
            new("Gpu", "NVIDIA", "Power", "GPU Package", 90f),
        };
        var snapshot = SensorPicker.Pick(sensors, null, Now);
        Assert.Equal(45.5, snapshot.CpuPackagePowerW!.Value, 1);
        Assert.Equal(4.2, snapshot.CpuMemoryPowerW!.Value, 1);
    }

    [Fact]
    public void SensorPickerHasNoCpuPowerWhenAbsentOrImplausible()
    {
        var amd = new List<RawSensor> { new("Cpu", "AMD", "Power", "Package", 60f), new("Cpu", "AMD", "Power", "CPU Package", 0f) };
        var snapshot = SensorPicker.Pick(amd, null, Now);
        Assert.Null(snapshot.CpuPackagePowerW);
        Assert.Null(snapshot.CpuMemoryPowerW);
    }
}
