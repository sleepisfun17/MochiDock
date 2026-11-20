using System;
using System.Collections.Generic;
using MochiDock.Core;
using Xunit;

public class SensorPickerTests
{
    static RawSensor S(string hw, string name, string type, string sensor, float? v) => new(hw, name, type, sensor, v);
    static readonly DateTimeOffset Now = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CpuTemperature_PrefersPackageOverCores()
    {
        var sensors = new List<RawSensor>
        {
            S("Cpu", "CPU", "Temperature", "Core #1", 70),
            S("Cpu", "CPU", "Temperature", "CPU Package", 55),
        };
        Assert.Equal(55, SensorPicker.PickCpuTemperature(sensors));
    }

    [Fact]
    public void CpuTemperature_FallsBackToHottestCore()
    {
        var sensors = new List<RawSensor>
        {
            S("Cpu", "CPU", "Temperature", "P-Core #1", 48),
            S("Cpu", "CPU", "Temperature", "E-Core #2", 61),
            S("Cpu", "CPU", "Temperature", "Distance to TjMax", 30),
            S("Cpu", "CPU", "Temperature", "P-Core #1 Distance to TjMax", 90),
        };
        Assert.Equal(61, SensorPicker.PickCpuTemperature(sensors));
    }

    [Fact]
    public void CpuTemperature_IgnoresInvalidAndMissingValues()
    {
        var sensors = new List<RawSensor>
        {
            S("Cpu", "CPU", "Temperature", "CPU Package", 0),
            S("Cpu", "CPU", "Temperature", "Core Max", float.NaN),
            S("Cpu", "CPU", "Temperature", "Tctl/Tdie", null),
        };
        Assert.Null(SensorPicker.PickCpuTemperature(sensors));
        Assert.Null(SensorPicker.PickCpuTemperature(new List<RawSensor>()));
    }

    [Fact]
    public void Gpu_PicksAdapterMatchingDetectedName()
    {
        var sensors = new List<RawSensor>
        {
            S("GpuAmd", "AMD Radeon(TM) Graphics", "Temperature", "GPU Core", 40),
            S("GpuAmd", "AMD Radeon RX 9070 XT", "Temperature", "GPU Core", 52),
            S("GpuAmd", "AMD Radeon RX 9070 XT", "Clock", "GPU Core", 2400),
            S("GpuAmd", "AMD Radeon RX 9070 XT", "Power", "GPU Package", 88),
        };
        var snap = SensorPicker.Pick(sensors, "AMD Radeon RX 9070 XT", Now);
        Assert.Equal(52, snap.GpuTempC);
        Assert.Equal(2400, snap.GpuClockMhz);
        Assert.Equal(88, snap.GpuPowerW);
        Assert.Equal("AMD Radeon RX 9070 XT", snap.GpuHardwareName);
        Assert.Equal(Now, snap.CapturedAt);
    }

    [Fact]
    public void Gpu_WithoutHintUsesFirstAdapterThatHasATemperature()
    {
        var sensors = new List<RawSensor>
        {
            S("GpuIntel", "Intel UHD", "Load", "GPU Core", 3),
            S("GpuNvidia", "NVIDIA RTX", "Temperature", "GPU Core", 60),
        };
        Assert.Equal(60, SensorPicker.Pick(sensors, null, Now).GpuTempC);
    }

    [Fact]
    public void Gpu_ZeroPowerAndClockAreNotTreatedAsReadings()
    {
        var sensors = new List<RawSensor>
        {
            S("GpuAmd", "AMD", "Temperature", "GPU Core", 45),
            S("GpuAmd", "AMD", "Clock", "GPU Core", 0),
            S("GpuAmd", "AMD", "Power", "GPU Package", 0),
        };
        var snap = SensorPicker.Pick(sensors, null, Now);
        Assert.Equal(45, snap.GpuTempC);
        Assert.Null(snap.GpuClockMhz);
        Assert.Null(snap.GpuPowerW);
    }

    [Fact]
    public void NoGpuSensors_ProducesNullGpuValues()
    {
        var snap = SensorPicker.Pick(new List<RawSensor> { S("Cpu", "CPU", "Temperature", "CPU Package", 50) }, "x", Now);
        Assert.Null(snap.GpuTempC);
        Assert.Null(snap.GpuHardwareName);
        Assert.Equal(50, snap.CpuTempC);
    }
}
