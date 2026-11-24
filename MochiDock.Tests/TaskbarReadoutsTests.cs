using System.Text.Json;
using System.Text.Json.Nodes;
using MochiDock.Core;
using MochiDock.Models;

public class TaskbarReadoutsTests
{
    private static TaskbarOptions All() => new() { System = true, MemoryDisk = true, Network = true, Prayer = true, Weather = true, Batteries = true };

    private static TaskbarSnapshot Full() => new()
    {
        CpuPercent = 2, CpuTempC = 45, CpuTempMeasured = true, GpuPercent = 5, GpuTempC = 37,
        RamPercent = 49, DiskPercent = 0, NetUpBytesPerSecond = 51 * 1024, NetDownBytesPerSecond = 18 * 1024,
        PrayerName = "Maghrib", PrayerTime = "16:40",
        WeatherTempC = 13, WindMetersPerSecond = 8, WeatherGlyph = "☁", WeatherCondition = "Cloudy",
        Batteries = new[]
        {
            new TaskbarBattery("M", "Mouse", "2.4G", 64, false, false, DateTimeOffset.UtcNow),
            new TaskbarBattery("K", "Keyboard", "Bluetooth", 82, false, false, DateTimeOffset.UtcNow),
        },
    };

    [Fact]
    public void BlocksComeInTheFixedOrder()
    {
        var kinds = TaskbarReadouts.Build(Full(), All()).Select(b => b.Kind).ToArray();
        Assert.Equal(new[] { TaskbarBlockKind.System, TaskbarBlockKind.MemoryDisk, TaskbarBlockKind.Network, TaskbarBlockKind.Prayer, TaskbarBlockKind.Weather, TaskbarBlockKind.Batteries }, kinds);
    }

    [Fact]
    public void TheExampleReadsLikeTheDesign()
    {
        var blocks = TaskbarReadouts.Build(Full(), All());
        Assert.Equal(("CPU 2% 45°", "GPU 5% 37°"), (blocks[0].Top, blocks[0].Bottom));
        Assert.Equal(("MEM 49%", "DISK 0%"), (blocks[1].Top, blocks[1].Bottom));
        Assert.Equal(("↑ 51 KB/s", "↓ 18 KB/s"), (blocks[2].Top, blocks[2].Bottom));
        Assert.Equal(("Maghrib", "16:40"), (blocks[3].Top, blocks[3].Bottom));
        Assert.Equal(("☁ 13°", "↘ 8 m/s"), (blocks[4].Top, blocks[4].Bottom));
        Assert.Equal(("64%", "82%"), (blocks[5].Top, blocks[5].Bottom));
    }

    [Fact]
    public void SwitchedOffBlocksAreSkipped()
    {
        var onlyNetwork = new TaskbarOptions { Network = true };
        var blocks = TaskbarReadouts.Build(Full(), onlyNetwork);
        Assert.Single(blocks);
        Assert.Equal(TaskbarBlockKind.Network, blocks[0].Kind);
        Assert.Empty(TaskbarReadouts.Build(Full(), new TaskbarOptions()));
    }

    [Fact]
    public void UnavailableBlocksAreSkippedNotShownEmpty()
    {
        var empty = new TaskbarSnapshot();   // no readings yet, no prayer times, no weather, no devices
        Assert.Empty(TaskbarReadouts.Build(empty, All()));

        var noGpu = TaskbarReadouts.Build(new TaskbarSnapshot { CpuPercent = 10, CpuTempC = 50, CpuTempMeasured = true }, new TaskbarOptions { System = true });
        Assert.Equal("CPU 10% 50°", noGpu[0].Top);
        Assert.False(noGpu[0].HasBottom);   // no GPU: a one-line block

        Assert.Empty(TaskbarReadouts.Build(new TaskbarSnapshot { PrayerName = "Asr" }, new TaskbarOptions { Prayer = true }));   // no time yet
        Assert.Empty(TaskbarReadouts.Build(new TaskbarSnapshot(), new TaskbarOptions { Weather = true }));   // no weather yet
    }

    [Fact]
    public void ACpuTemperatureEstimateIsMarkedAndNeverAWarning()
    {
        var snapshot = new TaskbarSnapshot { CpuPercent = 10, CpuTempC = 95, CpuTempMeasured = false };
        var block = TaskbarReadouts.Build(snapshot, new TaskbarOptions { System = true })[0];
        Assert.Equal("CPU 10% ~95°", block.Top);
        Assert.False(block.TopWarn);
        Assert.Contains("estimate", block.Tooltip);

        var measured = TaskbarReadouts.Build(snapshot with { CpuTempMeasured = true }, new TaskbarOptions { System = true })[0];
        Assert.Equal("CPU 10% 95°", measured.Top);
        Assert.True(measured.TopWarn);
    }

    [Theory]
    [InlineData(89, 20, false, false)]
    [InlineData(90, 20, true, false)]
    [InlineData(10, 20, false, false)]
    public void CpuAndGpuWarnAtTheirAlertThresholds(double cpu, double gpu, bool cpuWarn, bool gpuWarn)
    {
        var block = TaskbarReadouts.Build(new TaskbarSnapshot { CpuPercent = cpu, GpuPercent = gpu }, new TaskbarOptions { System = true, CpuAlert = 90, GpuAlert = 90 })[0];
        Assert.Equal(cpuWarn, block.TopWarn);
        Assert.Equal(gpuWarn, block.BottomWarn);
    }

    [Fact]
    public void TheThresholdsComeFromTheOptions()
    {
        var snapshot = new TaskbarSnapshot { CpuPercent = 60, GpuPercent = 70, GpuTempC = 80, CpuTempMeasured = true, CpuTempC = 70 };
        var strict = TaskbarReadouts.Build(snapshot, new TaskbarOptions { System = true, CpuAlert = 60, GpuAlert = 99, TempAlert = 80 })[0];
        Assert.True(strict.TopWarn);      // CPU load at its threshold
        Assert.True(strict.BottomWarn);   // GPU temperature at the temperature threshold
        var relaxed = TaskbarReadouts.Build(snapshot, new TaskbarOptions { System = true, CpuAlert = 61, GpuAlert = 71, TempAlert = 81 })[0];
        Assert.False(relaxed.TopWarn);
        Assert.False(relaxed.BottomWarn);
    }

    [Theory]
    [InlineData(89, false)]
    [InlineData(90, true)]
    public void MemoryWarnsAtNinetyPercent(double ram, bool warn)
    {
        var block = TaskbarReadouts.Build(new TaskbarSnapshot { RamPercent = ram, DiskPercent = 100 }, new TaskbarOptions { MemoryDisk = true })[0];
        Assert.Equal(warn, block.TopWarn);
        Assert.False(block.BottomWarn);   // a busy disk is not a warning
    }

    [Fact]
    public void DiskAloneStillMakesABlock()
    {
        var block = TaskbarReadouts.Build(new TaskbarSnapshot { DiskPercent = 12 }, new TaskbarOptions { MemoryDisk = true })[0];
        Assert.Equal("DISK 12%", block.Top);
        Assert.False(block.HasBottom);
    }

    [Theory]
    [InlineData(0, "0 KB/s")]
    [InlineData(51 * 1024, "51 KB/s")]
    [InlineData(1.2 * 1024 * 1024, "1.2 MB/s")]
    [InlineData(250 * 1024 * 1024, "250 MB/s")]
    public void RatesUseTheStatsCardUnits(double bytes, string text)
    {
        Assert.Equal(text, TaskbarReadouts.Rate(bytes));
        Assert.Equal(MochiDock.Core.RateScale.Format(bytes), TaskbarReadouts.Rate(bytes));
    }

    [Theory]
    [InlineData(2.4, "2%")]
    [InlineData(2.5, "3%")]
    [InlineData(100, "100%")]
    [InlineData(120, "100%")]
    [InlineData(-4, "0%")]
    public void PercentIsWhole(double value, string text) => Assert.Equal(text, TaskbarReadouts.Percent(value));

    [Theory]
    [InlineData(44.6, false, "45°")]
    [InlineData(44.6, true, "~45°")]
    [InlineData(-3, false, "-3°")]
    public void TemperaturesAndTheEstimateMarker(double c, bool estimate, string text) => Assert.Equal(text, TaskbarReadouts.Temperature(c, estimate));

    [Fact]
    public void WindIsWholeMetersPerSecond() => Assert.Equal("8 m/s", TaskbarReadouts.Wind(7.6));

    [Fact]
    public void WeatherWithoutAGlyphOrWindStillReads()
    {
        var block = TaskbarReadouts.Build(new TaskbarSnapshot { WeatherTempC = -2 }, new TaskbarOptions { Weather = true })[0];
        Assert.Equal("-2°", block.Top);
        Assert.False(block.HasBottom);
    }

    private static TaskbarBattery Device(string name, int? percent, bool charging = false, bool stale = false) =>
        new("G", name, "2.4G", percent, charging, stale, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(6, 2)]   // MaxItems (4) is kept
    public void BatteriesPackTwoToABlockAndKeepMaxItems(int devices, int blocks)
    {
        var batteries = Enumerable.Range(0, devices).Select(i => Device("D" + i, 50 + i)).ToArray();
        var result = TaskbarReadouts.Build(new TaskbarSnapshot { Batteries = batteries }, new TaskbarOptions { Batteries = true });
        Assert.Equal(blocks, result.Count);
        Assert.All(result, b => Assert.Equal(TaskbarBlockKind.Batteries, b.Kind));
        Assert.Equal(Math.Min(devices, TaskbarReadouts.MaxItems), result.Sum(b => b.HasBottom ? 2 : 1));
    }

    [Fact]
    public void ABatteryBlockCarriesItsGlyphChargingAndStaleFlags()
    {
        var block = TaskbarReadouts.Build(new TaskbarSnapshot { Batteries = new[] { Device("Mouse", 64, charging: true), Device("Keys", 10, stale: true) } },
            new TaskbarOptions { Batteries = true })[0];
        Assert.Equal("G", block.TopGlyph);
        Assert.True(block.TopCharging);
        Assert.False(block.TopDim);
        Assert.True(block.BottomDim);
        Assert.True(block.BottomWarn);    // 10%, not charging
        Assert.False(block.TopWarn);
        Assert.Contains("Mouse: 64%", block.Tooltip);
        Assert.Contains("charging", block.Tooltip);
        Assert.Contains("last seen", block.Tooltip);
    }

    [Fact]
    public void ACharginglowDeviceIsNotAWarningAndAnUnknownLevelReadsAsQuestionMark()
    {
        var blocks = TaskbarReadouts.Build(new TaskbarSnapshot { Batteries = new[] { Device("A", 5, charging: true), Device("B", null) } }, new TaskbarOptions { Batteries = true });
        Assert.False(blocks[0].TopWarn);
        Assert.Equal("?", blocks[0].Bottom);
    }

    [Fact]
    public void AClickableBlockHasATooltip()
    {
        foreach (var block in TaskbarReadouts.Build(Full(), All())) Assert.False(string.IsNullOrWhiteSpace(block.Tooltip), block.Kind.ToString());
    }

    // ---- the settings ----

    [Fact]
    public void TheTaskbarKeysAreLocalOnly()
    {
        foreach (var key in new[] { "TaskbarShowSystem", "TaskbarShowMemoryDisk", "TaskbarShowNetwork", "TaskbarShowPrayer", "TaskbarShowWeather" })
            Assert.Contains(key, SettingsTransfer.LocalOnlyKeys);

        var settings = new JsonObject { ["TaskbarShowNetwork"] = true, ["CapsuleTheme"] = "Matcha" };
        var exported = SettingsTransfer.BuildExport(settings, new JsonArray())["settings"] as JsonObject;
        Assert.False(exported!.ContainsKey("TaskbarShowNetwork"));
        Assert.True(exported.ContainsKey("CapsuleTheme"));

        // An import never switches the readouts on a PC that has them off.
        var merged = SettingsTransfer.Merge(new JsonObject { ["TaskbarShowNetwork"] = true }, new JsonObject { ["TaskbarShowNetwork"] = false });
        Assert.False(merged["TaskbarShowNetwork"]!.GetValue<bool>());
        Assert.False(SettingsTransfer.Merge(new JsonObject { ["TaskbarShowNetwork"] = true }, new JsonObject()).ContainsKey("TaskbarShowNetwork"));
    }

    [Fact]
    public void OldSettingsWithoutTheKeysLoadWithEverythingOff()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"CapsuleTheme\":\"Matcha\",\"ShowDeviceBatteriesOnTaskbar\":true}")!;
        Assert.False(settings.TaskbarShowSystem);
        Assert.False(settings.TaskbarShowMemoryDisk);
        Assert.False(settings.TaskbarShowNetwork);
        Assert.False(settings.TaskbarShowPrayer);
        Assert.False(settings.TaskbarShowWeather);
        Assert.True(settings.ShowDeviceBatteriesOnTaskbar);
        var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { TaskbarShowNetwork = true }))!;
        Assert.True(roundTrip.TaskbarShowNetwork);
    }

    [Fact]
    public void TheTaskbarSectionIsInTheSettingsWindowAndHasAnAlias()
    {
        Assert.Equal("Taskbar", SettingsSections.Find(SettingsSections.Taskbar)!.Title);
        Assert.Equal(SettingsSections.Taskbar, SettingsSections.Resolve("taskbar-readouts"));
        Assert.Equal(SettingsSections.Taskbar, SettingsSections.Resolve("TASKBAR"));
    }

    [Fact]
    public void NeedsSamplerOnlyForSampledBlocks()
    {
        Assert.False(new TaskbarOptions { Prayer = true, Weather = true, Batteries = true }.NeedsSampler);
        Assert.True(new TaskbarOptions { System = true }.NeedsSampler);
        Assert.True(new TaskbarOptions { MemoryDisk = true }.NeedsSampler);
        Assert.True(new TaskbarOptions { Network = true }.NeedsSampler);
    }
}

public class TaskbarSamplingTests
{
    [Fact]
    public void CpuLoadFromTwoReadings()
    {
        var cpu = new CpuLoadDelta();
        Assert.Null(cpu.Update(1000, 2000));         // the first reading has nothing to compare with
        Assert.Equal(50, cpu.Update(1500, 3000)!.Value, 6);   // 500 of 1000 ticks idle
        Assert.Equal(0, cpu.Update(2500, 4000)!.Value, 6);    // all idle
        Assert.Equal(100, cpu.Update(2500, 5000)!.Value, 6);  // no idle
    }

    [Fact]
    public void CpuLoadIgnoresACounterThatWentBackwardsAndResets()
    {
        var cpu = new CpuLoadDelta();
        cpu.Update(1000, 2000);
        Assert.Null(cpu.Update(900, 1900));
        cpu.Update(1000, 2000);
        cpu.Reset();
        Assert.Null(cpu.Update(2000, 4000));
    }

    // The taskbar sampler and the 24-hour history sampler each own their CPU and network state, so a reading by one never disturbs the other.
    [Fact]
    public void TwoSamplersKeepTheirOwnDeltas()
    {
        var history = new CpuLoadDelta();   // the 15 s history sampler's
        var taskbar = new CpuLoadDelta();   // the taskbar's, read every second
        history.Update(0, 0);
        taskbar.Update(0, 0);
        for (ulong t = 1; t <= 15; t++) Assert.NotNull(taskbar.Update(t * 100, t * 1000));   // 90% busy, every second
        // The history sampler's first delta still spans all 15 seconds, not just the last one.
        Assert.Equal(90, history.Update(1500, 15000)!.Value, 6);

        var historyNet = new ByteRateDelta();
        var taskbarNet = new ByteRateDelta();
        historyNet.Update(0, 0, 0);
        taskbarNet.Update(0, 0, 0);
        for (int s = 1; s <= 15; s++) Assert.NotNull(taskbarNet.Update(s, s * 1000, s * 100));
        var rate = historyNet.Update(15, 15000, 1500)!.Value;
        Assert.Equal(1000, rate.Down, 6);
        Assert.Equal(100, rate.Up, 6);
    }

    [Fact]
    public void ByteRatesFromSuccessiveTotals()
    {
        var net = new ByteRateDelta();
        Assert.Null(net.Update(10, 1_000, 500));
        var r = net.Update(12, 3_048, 1_500)!.Value;
        Assert.Equal(1024, r.Down, 6);
        Assert.Equal(500, r.Up, 6);
    }

    [Fact]
    public void ByteRatesSkipACounterResetAndAFrozenClock()
    {
        var net = new ByteRateDelta();
        net.Update(10, 5_000, 5_000);
        Assert.Null(net.Update(11, 100, 100));      // an adapter went away: the total fell
        Assert.Null(net.Update(11, 200, 200));      // the time did not advance
        Assert.NotNull(net.Update(12, 300, 300));
        net.Reset();
        Assert.Null(net.Update(13, 400, 400));
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(80, 20)]
    [InlineData(0, 100)]
    [InlineData(130, 0)]     // the counter can read over 100
    [InlineData(-5, 100)]
    public void DiskActivityIsTheIdleTimeInverted(double idle, double activity) => Assert.Equal(activity, TaskbarSampling.DiskActivityFromIdle(idle));

    [Fact]
    public void TheCpuTemperatureEstimateGrowsWithLoad()
    {
        Assert.True(TaskbarSampling.EstimateCpuTemperature(0) < TaskbarSampling.EstimateCpuTemperature(100));
        Assert.Equal(42, TaskbarSampling.EstimateCpuTemperature(0));
    }
}
