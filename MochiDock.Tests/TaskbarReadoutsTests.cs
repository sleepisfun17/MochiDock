using System.Text.Json;
using System.Text.Json.Nodes;
using MochiDock.Core;
using MochiDock.Models;

public class TaskbarReadoutsTests
{
    private static TaskbarOptions All() => new()
    {
        CpuLoad = true, CpuTemp = true, GpuLoad = true, GpuTemp = true,
        Memory = true, Disk = true, Upload = true, Download = true,
        Prayer = true, WeatherTemp = true, WeatherWind = true, Batteries = true,
    };

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
    public void BlocksComeInTheDefaultOrder()
    {
        var kinds = TaskbarReadouts.Build(Full(), All()).Select(b => b.Kind).ToArray();
        Assert.Equal(new[] { TaskbarBlockKind.System, TaskbarBlockKind.MemoryDisk, TaskbarBlockKind.Network, TaskbarBlockKind.Prayer, TaskbarBlockKind.Weather, TaskbarBlockKind.Batteries }, kinds);
    }

    [Fact]
    public void CustomBlockOrderIsRespectedAndNormalized()
    {
        var order = TaskbarReadouts.MoveBlock(TaskbarReadouts.DefaultOrder, TaskbarBlockKind.Network, -2);
        Assert.Equal(TaskbarBlockKind.Network, order[0]);
        Assert.Equal(TaskbarBlockKind.System, order[1]);

        var clampedUp = TaskbarReadouts.MoveBlock(order, TaskbarBlockKind.Network, -1);
        Assert.Equal(order, clampedUp);
        var clampedDown = TaskbarReadouts.MoveBlock(order, TaskbarBlockKind.Batteries, +1);
        Assert.Equal(order, clampedDown);

        var normalized = TaskbarReadouts.NormalizeOrder(new[] { "Weather", "bogus", "Weather", "Batteries" });
        Assert.Equal(new[]
        {
            TaskbarBlockKind.Weather, TaskbarBlockKind.Batteries,
            TaskbarBlockKind.System, TaskbarBlockKind.MemoryDisk, TaskbarBlockKind.Network, TaskbarBlockKind.Prayer,
        }, normalized);

        var blocks = TaskbarReadouts.Build(Full(), All() with { BlockOrder = new[] { TaskbarBlockKind.Batteries, TaskbarBlockKind.Network, TaskbarBlockKind.System } });
        Assert.Equal(new[] { TaskbarBlockKind.Batteries, TaskbarBlockKind.Network, TaskbarBlockKind.System }, blocks.Select(b => b.Kind).Take(3).ToArray());
    }

    [Fact]
    public void TheExampleReadsLikeTheDesign()
    {
        var blocks = TaskbarReadouts.Build(Full(), All());
        Assert.Equal(("CPU 2% 45°", "GPU 5% 37°"), (blocks[0].Top, blocks[0].Bottom));
        Assert.Equal(("CPU ", "GPU "), (blocks[0].TopLabel, blocks[0].BottomLabel));
        Assert.Equal(("2% 45°", "5% 37°"), (blocks[0].TopValue, blocks[0].BottomValue));
        Assert.Equal(("MEM 49%", "DISK 0%"), (blocks[1].Top, blocks[1].Bottom));
        Assert.Equal(("↑ 51 KB/s", "↓ 18 KB/s"), (blocks[2].Top, blocks[2].Bottom));
        Assert.Equal(("Maghrib", "16:40"), (blocks[3].Top, blocks[3].Bottom));
        Assert.Equal(("13°C", "↘ 8 m/s"), (blocks[4].Top, blocks[4].Bottom));
        Assert.Equal("2601", blocks[4].TopEmojiKey);
        Assert.Equal(("64%", "82%"), (blocks[5].Top, blocks[5].Bottom));
    }

    [Fact]
    public void IndividualItemsCanBeToggledAndSingleLinesPromoteToTop()
    {
        var cpuLoadOnly = TaskbarReadouts.Build(Full(), new TaskbarOptions { CpuLoad = true });
        Assert.Equal("CPU 2%", Assert.Single(cpuLoadOnly).Top);
        Assert.False(cpuLoadOnly[0].HasBottom);

        var cpuTempOnly = TaskbarReadouts.Build(Full(), new TaskbarOptions { CpuTemp = true });
        Assert.Equal("CPU 45°", Assert.Single(cpuTempOnly).Top);

        var gpuOnly = TaskbarReadouts.Build(Full(), new TaskbarOptions { GpuLoad = true, GpuTemp = true });
        Assert.Equal("GPU 5% 37°", Assert.Single(gpuOnly).Top);
        Assert.Equal("GPU ", gpuOnly[0].TopLabel);
        Assert.False(gpuOnly[0].HasBottom);

        var gpuTempOnly = TaskbarReadouts.Build(Full(), new TaskbarOptions { GpuTemp = true });
        Assert.Equal("GPU 37°", Assert.Single(gpuTempOnly).Top);

        var diskOnly = TaskbarReadouts.Build(Full(), new TaskbarOptions { Disk = true });
        Assert.Equal("DISK 0%", Assert.Single(diskOnly).Top);

        var downOnly = TaskbarReadouts.Build(Full(), new TaskbarOptions { Download = true, NetworkArrows = TaskbarArrowStyle.Triangle });
        Assert.Equal("▼ 18 KB/s", Assert.Single(downOnly).Top);
        Assert.False(downOnly[0].HasBottom);

        var windOnly = TaskbarReadouts.Build(Full(), new TaskbarOptions { WeatherWind = true });
        Assert.Equal("↘ 8 m/s", Assert.Single(windOnly).Top);
    }

    [Fact]
    public void NetworkArrowStylesProduceExpectedLabelsAndEmojiKeys()
    {
        var tri = Assert.Single(TaskbarReadouts.Build(Full(), new TaskbarOptions { Upload = true, Download = true, NetworkArrows = TaskbarArrowStyle.Triangle }));
        Assert.Equal(("▲ 51 KB/s", "▼ 18 KB/s"), (tri.Top, tri.Bottom));
        Assert.Equal(("▲ ", "▼ "), (tri.TopLabel, tri.BottomLabel));
        Assert.Equal(("", ""), (tri.TopEmojiKey, tri.BottomEmojiKey));

        var emoji = Assert.Single(TaskbarReadouts.Build(Full(), new TaskbarOptions { Upload = true, Download = true, NetworkArrows = TaskbarArrowStyle.Emoji }));
        Assert.Equal(("51 KB/s", "18 KB/s"), (emoji.Top, emoji.Bottom));
        Assert.Equal((TaskbarReadouts.UploadEmojiKey, TaskbarReadouts.DownloadEmojiKey), (emoji.TopEmojiKey, emoji.BottomEmojiKey));

        var downEmojiOnly = Assert.Single(TaskbarReadouts.Build(Full(), new TaskbarOptions { Download = true, NetworkArrows = TaskbarArrowStyle.Emoji }));
        Assert.Equal("18 KB/s", downEmojiOnly.Top);
        Assert.Equal(TaskbarReadouts.DownloadEmojiKey, downEmojiOnly.TopEmojiKey);
        Assert.False(downEmojiOnly.HasBottom);
    }

    [Fact]
    public void SwitchedOffBlocksAreSkipped()
    {
        var onlyNetwork = new TaskbarOptions { Upload = true, Download = true };
        var blocks = TaskbarReadouts.Build(Full(), onlyNetwork);
        Assert.Single(blocks);
        Assert.Equal(TaskbarBlockKind.Network, blocks[0].Kind);
        Assert.Empty(TaskbarReadouts.Build(Full(), new TaskbarOptions()));
    }

    [Fact]
    public void UnavailableBlocksAreSkippedNotShownEmpty()
    {
        var empty = new TaskbarSnapshot();
        Assert.Empty(TaskbarReadouts.Build(empty, All()));

        var noGpu = TaskbarReadouts.Build(new TaskbarSnapshot { CpuPercent = 10, CpuTempC = 50, CpuTempMeasured = true },
            new TaskbarOptions { CpuLoad = true, CpuTemp = true, GpuLoad = true, GpuTemp = true });
        Assert.Equal("CPU 10% 50°", noGpu[0].Top);
        Assert.False(noGpu[0].HasBottom);

        Assert.Empty(TaskbarReadouts.Build(new TaskbarSnapshot { PrayerName = "Asr" }, new TaskbarOptions { Prayer = true }));
        Assert.Empty(TaskbarReadouts.Build(new TaskbarSnapshot(), new TaskbarOptions { WeatherTemp = true, WeatherWind = true }));
    }

    [Fact]
    public void ACpuTemperatureEstimateIsMarkedAndNeverAWarning()
    {
        var snapshot = new TaskbarSnapshot { CpuPercent = 10, CpuTempC = 95, CpuTempMeasured = false };
        var opts = new TaskbarOptions { CpuLoad = true, CpuTemp = true };
        var block = TaskbarReadouts.Build(snapshot, opts)[0];
        Assert.Equal("CPU 10% ~95°", block.Top);
        Assert.False(block.TopWarn);
        Assert.Contains("estimate", block.Tooltip);

        var measured = TaskbarReadouts.Build(snapshot with { CpuTempMeasured = true }, opts)[0];
        Assert.Equal("CPU 10% 95°", measured.Top);
        Assert.True(measured.TopWarn);
    }

    [Theory]
    [InlineData(89, 20, false, false)]
    [InlineData(90, 20, true, false)]
    [InlineData(10, 20, false, false)]
    public void CpuAndGpuWarnAtTheirAlertThresholds(double cpu, double gpu, bool cpuWarn, bool gpuWarn)
    {
        var block = TaskbarReadouts.Build(new TaskbarSnapshot { CpuPercent = cpu, GpuPercent = gpu },
            new TaskbarOptions { CpuLoad = true, GpuLoad = true, CpuAlert = 90, GpuAlert = 90 })[0];
        Assert.Equal(cpuWarn, block.TopWarn);
        Assert.Equal(gpuWarn, block.BottomWarn);
    }

    [Fact]
    public void TheThresholdsComeFromTheOptions()
    {
        var snapshot = new TaskbarSnapshot { CpuPercent = 60, GpuPercent = 70, GpuTempC = 80, CpuTempMeasured = true, CpuTempC = 70 };
        var strict = TaskbarReadouts.Build(snapshot, new TaskbarOptions { CpuLoad = true, CpuTemp = true, GpuLoad = true, GpuTemp = true, CpuAlert = 60, GpuAlert = 99, TempAlert = 80 })[0];
        Assert.True(strict.TopWarn);
        Assert.True(strict.BottomWarn);
        var relaxed = TaskbarReadouts.Build(snapshot, new TaskbarOptions { CpuLoad = true, CpuTemp = true, GpuLoad = true, GpuTemp = true, CpuAlert = 61, GpuAlert = 71, TempAlert = 81 })[0];
        Assert.False(relaxed.TopWarn);
        Assert.False(relaxed.BottomWarn);
    }

    [Theory]
    [InlineData(89, false)]
    [InlineData(90, true)]
    public void MemoryWarnsAtNinetyPercent(double ram, bool warn)
    {
        var block = TaskbarReadouts.Build(new TaskbarSnapshot { RamPercent = ram, DiskPercent = 100 }, new TaskbarOptions { Memory = true, Disk = true })[0];
        Assert.Equal(warn, block.TopWarn);
        Assert.False(block.BottomWarn);
    }

    [Fact]
    public void DiskAloneStillMakesABlock()
    {
        var block = TaskbarReadouts.Build(new TaskbarSnapshot { DiskPercent = 12 }, new TaskbarOptions { Memory = true, Disk = true })[0];
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
        Assert.Equal(RateScale.Format(bytes), TaskbarReadouts.Rate(bytes));
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
        var block = TaskbarReadouts.Build(new TaskbarSnapshot { WeatherTempC = -2 }, new TaskbarOptions { WeatherTemp = true, WeatherWind = true })[0];
        Assert.Equal("-2°C", block.Top);
        Assert.False(block.HasBottom);
    }

    private static TaskbarBattery Device(string name, int? percent, bool charging = false, bool stale = false) =>
        new("G", name, "2.4G", percent, charging, stale, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(6, 2)]
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
        Assert.True(block.BottomWarn);
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
    public void HorizontalBatteryLayoutEmitsOneBlockPerDeviceUpToMaxItems()
    {
        var batteries = Enumerable.Range(0, 6).Select(i => Device("D" + i, 50 + i, charging: i == 1)).ToArray();
        var horiz = TaskbarReadouts.Build(new TaskbarSnapshot { Batteries = batteries },
            new TaskbarOptions { Batteries = true, BatteryLayout = TaskbarBatteryLayout.Horizontal });
        Assert.Equal(TaskbarReadouts.MaxItems, horiz.Count);
        Assert.All(horiz, b => Assert.False(b.HasBottom));
        Assert.True(horiz[1].TopCharging);

        var singleAuto = Assert.Single(TaskbarReadouts.Build(new TaskbarSnapshot { Batteries = new[] { Device("Mouse", 39) } },
            new TaskbarOptions { Batteries = true, BatteryLayout = TaskbarBatteryLayout.Auto }));
        Assert.Equal("39%", singleAuto.Top);
        Assert.False(singleAuto.HasBottom);
    }

    [Fact]
    public void AClickableBlockHasATooltip()
    {
        foreach (var block in TaskbarReadouts.Build(Full(), All())) Assert.False(string.IsNullOrWhiteSpace(block.Tooltip), block.Kind.ToString());
    }

    // ---- style, colours & placement ----

    [Fact]
    public void AutoFontScalesUpOnTallWindhawkTaskbarAndExplicitFontClampsToFit()
    {
        double f40 = TaskbarStyle.ComputeFontSize(40, 1.33, oneLine: false, requestedSize: 0);
        double f48 = TaskbarStyle.ComputeFontSize(48, 1.33, oneLine: false, requestedSize: 0);
        double f64 = TaskbarStyle.ComputeFontSize(64, 1.33, oneLine: false, requestedSize: 0);
        double f30 = TaskbarStyle.ComputeFontSize(30, 1.33, oneLine: true, requestedSize: 0);

        Assert.True(f48 > f40);
        Assert.True(f64 > f48);
        Assert.True(f64 <= TaskbarStyle.MaxAutoFontSize);
        Assert.True(f30 <= 12);

        double oversizedOn40 = TaskbarStyle.ComputeFontSize(40, 1.33, oneLine: false, requestedSize: 26);
        Assert.True(oversizedOn40 * 2 * 1.33 <= 40 - 3.9);
        Assert.Equal(16, TaskbarStyle.ComputeFontSize(64, 1.33, oneLine: false, requestedSize: 16), 3);
    }

    [Fact]
    public void CustomColorsPreserveContrastAcrossLightAndDarkTaskbars()
    {
        Assert.Equal((255, 255, 255), TaskbarStyle.ResolveColor("", darkTaskbar: true));
        Assert.Equal((0x1B, 0x1B, 0x1B), TaskbarStyle.ResolveColor("", darkTaskbar: false));
        Assert.Equal("#80CBC4", TaskbarStyle.NormalizeHexColor("80cbc4"));
        Assert.Equal("", TaskbarStyle.NormalizeHexColor("not-a-color"));

        // Bright teal passes directly on dark taskbar, and adapts darker on light taskbar to stay >= 4.5:1.
        var onDark = TaskbarStyle.ResolveColor("#80CBC4", darkTaskbar: true);
        Assert.Equal((0x80, 0xCB, 0xC4), onDark);
        var onLight = TaskbarStyle.ResolveColor("#80CBC4", darkTaskbar: false);
        Assert.NotEqual((0x80, 0xCB, 0xC4), onLight);
        Assert.True(TaskbarStyle.Contrast(onLight, (0xF3, 0xF3, 0xF3)) >= TaskbarStyle.MinContrastRatio);

        // Deep navy passes directly on light taskbar, and adapts lighter on dark taskbar to stay >= 4.5:1.
        var navyLight = TaskbarStyle.ResolveColor("#1A237E", darkTaskbar: false);
        Assert.Equal((0x1A, 0x23, 0x7E), navyLight);
        var navyDark = TaskbarStyle.ResolveColor("#1A237E", darkTaskbar: true);
        Assert.True(TaskbarStyle.Contrast(navyDark, (0x20, 0x20, 0x20)) >= TaskbarStyle.MinContrastRatio);
    }

    [Fact]
    public void PlacementComputesLeftOfTrayAndAfterTaskButtonsForLtrAndRtl()
    {
        // LTR LeftOfTray without widgets
        int x1 = TaskbarLayout.ComputeClientX(TaskbarPlacementMode.LeftOfTray, 0, rtl: false,
            taskbarLeft: 0, taskbarRight: 1920, trayLeft: 1700, trayRight: 1920, widgetWidth: 200, gapPx: 8,
            widgetsButton: null, taskButtons: (100, 600));
        Assert.Equal(1500, x1);

        // LTR LeftOfTray steps past WidgetsButton next to tray
        int x2 = TaskbarLayout.ComputeClientX(TaskbarPlacementMode.LeftOfTray, 0, rtl: false,
            taskbarLeft: 0, taskbarRight: 1920, trayLeft: 1700, trayRight: 1920, widgetWidth: 200, gapPx: 8,
            widgetsButton: (1580, 1700), taskButtons: (100, 600));
        Assert.Equal(1380, x2);

        // LTR AfterTaskButtons places right after task buttons + gap + offset
        int x3 = TaskbarLayout.ComputeClientX(TaskbarPlacementMode.AfterTaskButtons, 10, rtl: false,
            taskbarLeft: 0, taskbarRight: 1920, trayLeft: 1700, trayRight: 1920, widgetWidth: 200, gapPx: 8,
            widgetsButton: null, taskButtons: (100, 600));
        Assert.Equal(618, x3);

        // Crowded LTR AfterTaskButtons stops before WidgetsButton/Tray
        int xCrowded = TaskbarLayout.ComputeClientX(TaskbarPlacementMode.AfterTaskButtons, 0, rtl: false,
            taskbarLeft: 0, taskbarRight: 1920, trayLeft: 1700, trayRight: 1920, widgetWidth: 200, gapPx: 8,
            widgetsButton: (1580, 1700), taskButtons: (100, 1500));
        Assert.Equal(1380, xCrowded);

        // Fallback to LeftOfTray when taskButtons is null
        int xFallback = TaskbarLayout.ComputeClientX(TaskbarPlacementMode.AfterTaskButtons, 0, rtl: false,
            taskbarLeft: 0, taskbarRight: 1920, trayLeft: 1700, trayRight: 1920, widgetWidth: 200, gapPx: 8,
            widgetsButton: null, taskButtons: null);
        Assert.Equal(1500, xFallback);

        // RTL LeftOfTray & AfterTaskButtons
        int rtlTray = TaskbarLayout.ComputeClientX(TaskbarPlacementMode.LeftOfTray, 0, rtl: true,
            taskbarLeft: 0, taskbarRight: 1920, trayLeft: 0, trayRight: 220, widgetWidth: 200, gapPx: 8,
            widgetsButton: null, taskButtons: (1300, 1820));
        Assert.Equal(1920 - 220 - 200, rtlTray);

        int rtlTasks = TaskbarLayout.ComputeClientX(TaskbarPlacementMode.AfterTaskButtons, 0, rtl: true,
            taskbarLeft: 0, taskbarRight: 1920, trayLeft: 0, trayRight: 220, widgetWidth: 200, gapPx: 8,
            widgetsButton: null, taskButtons: (1300, 1820));
        // screenLeft = 1300 - 8 - 200 = 1092 => client x = 1920 - 1092 - 200 = 628
        Assert.Equal(628, rtlTasks);
    }

    [Fact]
    public void LeftZonePlacementClearsWidgetsButtonAndClampsBounds()
    {
        // LTR without widgets
        Assert.Equal(8, TaskbarLayout.ComputeLeftZoneClientX(0, rtl: false, taskbarLeft: 0, taskbarRight: 1920, widgetWidth: 200, gapPx: 8, widgetsButton: null));
        // LTR with left Widgets button (8..130): steps past 130 + 8 = 138
        Assert.Equal(138, TaskbarLayout.ComputeLeftZoneClientX(0, rtl: false, taskbarLeft: 0, taskbarRight: 1920, widgetWidth: 200, gapPx: 8, widgetsButton: (8, 130)));
        // Negative offset clamps to taskbarLeft (client 0)
        Assert.Equal(0, TaskbarLayout.ComputeLeftZoneClientX(-50, rtl: false, taskbarLeft: 0, taskbarRight: 1920, widgetWidth: 200, gapPx: 8, widgetsButton: null));
        // RTL without widgets: sits 8 px inside right leading edge -> client x = 8
        Assert.Equal(8, TaskbarLayout.ComputeLeftZoneClientX(0, rtl: true, taskbarLeft: 0, taskbarRight: 1920, widgetWidth: 200, gapPx: 8, widgetsButton: null));
        // RTL with Widgets button on leading right edge (1780..1912)
        Assert.Equal(148, TaskbarLayout.ComputeLeftZoneClientX(0, rtl: true, taskbarLeft: 0, taskbarRight: 1920, widgetWidth: 200, gapPx: 8, widgetsButton: (1780, 1912)));
    }

    [Fact]
    public void MonitorSelectionHandlesPrimaryFollowDockSpecificAndAll()
    {
        var screens = new[] { (@"\\.\DISPLAY1", true), (@"\\.\DISPLAY2", false) };

        Assert.Equal(new[] { 0 }, TaskbarMonitorSelection.SelectIndices("", @"\\.\DISPLAY2", screens));
        Assert.Equal(new[] { 1 }, TaskbarMonitorSelection.SelectIndices("Dock", @"\\.\DISPLAY2", screens));
        Assert.Equal(new[] { 1 }, TaskbarMonitorSelection.SelectIndices(@"\\.\display2", @"\\.\DISPLAY1", screens));
        Assert.Equal(new[] { 0 }, TaskbarMonitorSelection.SelectIndices(@"\\.\DISPLAY9", @"\\.\DISPLAY1", screens)); // disconnected fallback
        Assert.Equal(new[] { 0, 1 }, TaskbarMonitorSelection.SelectIndices("All", @"\\.\DISPLAY1", screens));
    }

    [Fact]
    public void BlocksSplitIntoLeftAndRightZonesInConfiguredOrder()
    {
        var zones = TaskbarReadouts.ParseZones(new Dictionary<string, string>
        {
            ["System"] = "Left",
            ["Network"] = "Left",
            ["Prayer"] = "Right",
        });
        var opts = All() with
        {
            BlockZones = zones,
            BlockOrder = new[]
            {
                TaskbarBlockKind.Network, TaskbarBlockKind.System, TaskbarBlockKind.Batteries,
                TaskbarBlockKind.Prayer, TaskbarBlockKind.Weather, TaskbarBlockKind.MemoryDisk,
            },
        };
        var (left, right) = TaskbarReadouts.BuildSplit(Full(), opts);
        Assert.Equal(new[] { TaskbarBlockKind.Network, TaskbarBlockKind.System }, left.Select(b => b.Kind).ToArray());
        Assert.Equal(new[] { TaskbarBlockKind.Batteries, TaskbarBlockKind.Prayer, TaskbarBlockKind.Weather, TaskbarBlockKind.MemoryDisk },
            right.Select(b => b.Kind).ToArray());
    }

    [Fact]
    public void CompactLabelsBitsUnitAndSwappedNetworkLinesWork()
    {
        Assert.Equal("0 Kb/s", TaskbarReadouts.RateBits(0));
        Assert.Equal("408 Kb/s", TaskbarReadouts.RateBits(51_000));
        Assert.Equal("9.6 Mb/s", TaskbarReadouts.RateBits(1_200_000));
        Assert.Equal("1.2 Gb/s", TaskbarReadouts.RateBits(150_000_000));

        var opts = new TaskbarOptions
        {
            CpuLoad = true, GpuLoad = true, Memory = true, Disk = true,
            Upload = true, Download = true,
            CompactLabels = true,
            NetworkUnit = TaskbarNetworkUnit.Bits,
            SwapNetworkLines = true,
        };
        var snap = new TaskbarSnapshot
        {
            CpuPercent = 12, GpuPercent = 34, RamPercent = 55, DiskPercent = 4,
            NetUpBytesPerSecond = 51_000, NetDownBytesPerSecond = 1_200_000,
        };
        var blocks = TaskbarReadouts.Build(snap, opts);
        Assert.Equal(("C 12%", "G 34%"), (blocks[0].Top, blocks[0].Bottom));
        Assert.Equal(("M 55%", "D 4%"), (blocks[1].Top, blocks[1].Bottom));
        Assert.Equal(("↓ 9.6 Mb/s", "↑ 408 Kb/s"), (blocks[2].Top, blocks[2].Bottom));

        // Single-line swapped promotion still promotes whichever is enabled
        var upOnlySwapped = Assert.Single(TaskbarReadouts.Build(snap, new TaskbarOptions { Upload = true, SwapNetworkLines = true, NetworkUnit = TaskbarNetworkUnit.Bits }));
        Assert.Equal("↑ 408 Kb/s", upOnlySwapped.Top);
        Assert.False(upOnlySwapped.HasBottom);
    }

    [Fact]
    public void SpacingVerticalOffsetAndRefreshClampsStayInRange()
    {
        Assert.Equal(0, TaskbarStyle.ClampBlockSpacing(0));
        Assert.Equal(4, TaskbarStyle.ClampBlockSpacing(2));
        Assert.Equal(40, TaskbarStyle.ClampBlockSpacing(99));
        Assert.Equal(-24, TaskbarStyle.ClampVerticalOffset(-100));
        Assert.Equal(24, TaskbarStyle.ClampVerticalOffset(100));
        Assert.Equal(1, TaskbarStyle.ClampRefreshSeconds(0));
        Assert.Equal(2, TaskbarStyle.ClampRefreshSeconds(2));
        Assert.Equal(3, TaskbarStyle.ClampRefreshSeconds(10));
    }

    [Fact]
    public void PrayerDisplayModesFormatRemainingAndHighlightLastTenMinutes()
    {
        Assert.Equal(TaskbarPrayerDisplay.TimeAndRemaining, TaskbarStyle.ParsePrayerDisplay(null));
        Assert.Equal(TaskbarPrayerDisplay.Remaining, TaskbarStyle.ParsePrayerDisplay("Remaining"));
        Assert.Equal(TaskbarPrayerDisplay.Time, TaskbarStyle.ParsePrayerDisplay("Time"));
        Assert.Equal("in 1h 25m", TaskbarReadouts.FormatPrayerRemaining(TimeSpan.FromMinutes(85)));
        Assert.Equal("in 10m", TaskbarReadouts.FormatPrayerRemaining(TimeSpan.FromMinutes(10)));
        Assert.Equal("<1m", TaskbarReadouts.FormatPrayerRemaining(TimeSpan.FromSeconds(25)));
        Assert.Equal("now", TaskbarReadouts.FormatPrayerRemaining(TimeSpan.Zero));

        var snap = new TaskbarSnapshot { PrayerName = "Maghrib", PrayerTime = "16:40", PrayerRemaining = TimeSpan.FromMinutes(85) };
        var both = Assert.Single(TaskbarReadouts.Build(snap, new TaskbarOptions { Prayer = true }));
        Assert.Equal(("Maghrib 16:40", "in 1h 25m"), (both.Top, both.Bottom));
        Assert.Equal("Maghrib ", both.TopLabel);
        Assert.Equal("16:40", both.TopValue);
        Assert.False(both.BottomWarn);
        Assert.Contains("in 1h 25m", both.Tooltip);

        var remOnly = Assert.Single(TaskbarReadouts.Build(snap, new TaskbarOptions { Prayer = true, PrayerDisplay = TaskbarPrayerDisplay.Remaining }));
        Assert.Equal(("Maghrib", "in 1h 25m"), (remOnly.Top, remOnly.Bottom));

        var timeOnly = Assert.Single(TaskbarReadouts.Build(snap, new TaskbarOptions { Prayer = true, PrayerDisplay = TaskbarPrayerDisplay.Time }));
        Assert.Equal(("Maghrib", "16:40"), (timeOnly.Top, timeOnly.Bottom));

        var warn10 = Assert.Single(TaskbarReadouts.Build(snap with { PrayerRemaining = TimeSpan.FromMinutes(10) }, new TaskbarOptions { Prayer = true }));
        Assert.True(warn10.BottomWarn);
        var noWarn11 = Assert.Single(TaskbarReadouts.Build(snap with { PrayerRemaining = TimeSpan.FromMinutes(11) }, new TaskbarOptions { Prayer = true }));
        Assert.False(noWarn11.BottomWarn);

        // Even in Time only mode, an approaching prayer within PrayerWarnMinutes switches bottom to the highlighted countdown.
        var timeModeClose = Assert.Single(TaskbarReadouts.Build(
            snap with { PrayerRemaining = TimeSpan.FromMinutes(8) },
            new TaskbarOptions { Prayer = true, PrayerDisplay = TaskbarPrayerDisplay.Time, PrayerWarnMinutes = 10 }));
        Assert.Equal(("Maghrib", "in 8m"), (timeModeClose.Top, timeModeClose.Bottom));
        Assert.True(timeModeClose.BottomWarn);

        var timeModeApproachOff = Assert.Single(TaskbarReadouts.Build(
            snap with { PrayerRemaining = TimeSpan.FromMinutes(8) },
            new TaskbarOptions { Prayer = true, PrayerDisplay = TaskbarPrayerDisplay.Time, PrayerWarnMinutes = 0 }));
        Assert.Equal(("Maghrib", "16:40"), (timeModeApproachOff.Top, timeModeApproachOff.Bottom));
        Assert.False(timeModeApproachOff.BottomWarn);
    }

    [Fact]
    public void CurrentPrayerElapsedShowsWithinWindowAndYieldsWhenApproachingOrExpired()
    {
        Assert.Equal("Now", TaskbarReadouts.FormatPrayerElapsed(TimeSpan.FromSeconds(25)));
        Assert.Equal("+5m", TaskbarReadouts.FormatPrayerElapsed(TimeSpan.FromMinutes(5)));
        Assert.Equal("+1h 5m", TaskbarReadouts.FormatPrayerElapsed(TimeSpan.FromMinutes(65)));

        var baseSnap = new TaskbarSnapshot
        {
            PrayerName = "Maghrib",
            PrayerTime = "16:48",
            PrayerRemaining = TimeSpan.FromMinutes(136),
            CurrentPrayerName = "Asr",
            CurrentPrayerTime = "14:27",
            CurrentPrayerElapsed = TimeSpan.FromMinutes(5),
        };

        var activeTimeMode = Assert.Single(TaskbarReadouts.Build(
            baseSnap,
            new TaskbarOptions { Prayer = true, PrayerDisplay = TaskbarPrayerDisplay.Time, PrayerElapsedMinutes = 15 }));
        Assert.Equal(("Asr", "+5m"), (activeTimeMode.Top, activeTimeMode.Bottom));
        Assert.False(activeTimeMode.BottomWarn);
        Assert.Contains("Current prayer: Asr (14:27, +5m)", activeTimeMode.Tooltip);
        Assert.Contains("Next prayer: Maghrib at 16:48", activeTimeMode.Tooltip);

        var activeBothMode = Assert.Single(TaskbarReadouts.Build(
            baseSnap with { CurrentPrayerElapsed = TimeSpan.FromSeconds(15) },
            new TaskbarOptions { Prayer = true, PrayerDisplay = TaskbarPrayerDisplay.TimeAndRemaining, PrayerElapsedMinutes = 15 }));
        Assert.Equal(("Asr 14:27", "Now"), (activeBothMode.Top, activeBothMode.Bottom));

        var expired = Assert.Single(TaskbarReadouts.Build(
            baseSnap with { CurrentPrayerElapsed = TimeSpan.FromMinutes(16) },
            new TaskbarOptions { Prayer = true, PrayerDisplay = TaskbarPrayerDisplay.Time, PrayerElapsedMinutes = 15 }));
        Assert.Equal(("Maghrib", "16:48"), (expired.Top, expired.Bottom));

        var disabled = Assert.Single(TaskbarReadouts.Build(
            baseSnap,
            new TaskbarOptions { Prayer = true, PrayerDisplay = TaskbarPrayerDisplay.Time, PrayerElapsedMinutes = 0 }));
        Assert.Equal(("Maghrib", "16:48"), (disabled.Top, disabled.Bottom));

        // If approaching window and elapsed window overlap, the upcoming countdown wins.
        var approachWins = Assert.Single(TaskbarReadouts.Build(
            baseSnap with { PrayerRemaining = TimeSpan.FromMinutes(7) },
            new TaskbarOptions { Prayer = true, PrayerDisplay = TaskbarPrayerDisplay.Time, PrayerWarnMinutes = 10, PrayerElapsedMinutes = 15 }));
        Assert.Equal(("Maghrib", "in 7m"), (approachWins.Top, approachWins.Bottom));
        Assert.True(approachWins.BottomWarn);
    }

    [Fact]
    public void WeatherShowsCelsiusAndSupportsEmojiOrMonochromeIcon()
    {
        Assert.Equal(TaskbarWeatherIconStyle.Emoji, TaskbarStyle.ParseWeatherIconStyle(null));
        Assert.Equal(TaskbarWeatherIconStyle.Monochrome, TaskbarStyle.ParseWeatherIconStyle("Monochrome"));
        Assert.Equal("Emoji", TaskbarStyle.FormatWeatherIconStyle(TaskbarWeatherIconStyle.Emoji));
        Assert.Equal("Monochrome", TaskbarStyle.FormatWeatherIconStyle(TaskbarWeatherIconStyle.Monochrome));

        Assert.Equal("2600", TaskbarReadouts.WeatherEmojiKey("☀️"));
        Assert.Equal("2600", TaskbarReadouts.WeatherEmojiKey("☀"));
        Assert.Equal("1f327", TaskbarReadouts.WeatherEmojiKey("🌧️"));
        Assert.Equal("26c5", TaskbarReadouts.WeatherEmojiKey("⛅"));

        var snap = new TaskbarSnapshot { WeatherTempC = 13, WindMetersPerSecond = 4, WeatherGlyph = "🌧️", WeatherCondition = "Rain" };
        var emojiBlock = Assert.Single(TaskbarReadouts.Build(snap, new TaskbarOptions { WeatherTemp = true, WeatherWind = true, WeatherIconStyle = TaskbarWeatherIconStyle.Emoji }));
        Assert.Equal(("13°C", "↘ 4 m/s"), (emojiBlock.Top, emojiBlock.Bottom));
        Assert.Equal("1f327", emojiBlock.TopEmojiKey);
        Assert.Equal("", emojiBlock.TopLabel);
        Assert.Contains("13°C", emojiBlock.Tooltip);

        var monoBlock = Assert.Single(TaskbarReadouts.Build(snap, new TaskbarOptions { WeatherTemp = true, WeatherWind = true, WeatherIconStyle = TaskbarWeatherIconStyle.Monochrome }));
        Assert.Equal(("🌧️ 13°C", "↘ 4 m/s"), (monoBlock.Top, monoBlock.Bottom));
        Assert.Equal("", monoBlock.TopEmojiKey);
        Assert.Equal("🌧️ ", monoBlock.TopLabel);

        // Daytime Clear matches the dock's cloud-sun; nighttime Clear switches to crescent moon.
        Assert.Equal("⛅", TaskbarReadouts.ResolveWeatherGlyph("Clear", "☀️", isNight: false));
        Assert.Equal("26c5", TaskbarReadouts.WeatherEmojiKey(TaskbarReadouts.ResolveWeatherGlyph("Clear", "☀️", isNight: false)));
        Assert.Equal("🌙", TaskbarReadouts.ResolveWeatherGlyph("Clear", "☀️", isNight: true));
        Assert.Equal("1f319", TaskbarReadouts.WeatherEmojiKey(TaskbarReadouts.ResolveWeatherGlyph("Clear", "☀️", isNight: true)));
        Assert.Equal("⛅", TaskbarReadouts.ResolveWeatherGlyph("PartlyCloudy", "🌤️", isNight: false));
        Assert.Equal("☁️", TaskbarReadouts.ResolveWeatherGlyph("PartlyCloudy", "🌤️", isNight: true));
        Assert.Equal("🌧️", TaskbarReadouts.ResolveWeatherGlyph("Rain", "🌧️", isNight: true));
    }

    // ---- settings, migration & local-only keys ----

    [Fact]
    public void TheTaskbarKeysAreLocalOnly()
    {
        foreach (var key in new[]
        {
            "TaskbarShowCpuLoad", "TaskbarShowCpuTemp", "TaskbarShowGpuLoad", "TaskbarShowGpuTemp",
            "TaskbarShowMemory", "TaskbarShowDisk", "TaskbarShowUpload", "TaskbarShowDownload",
            "TaskbarShowPrayer", "TaskbarShowWeatherTemp", "TaskbarShowWeatherWind",
            "TaskbarBlockOrder", "TaskbarBlockZones", "TaskbarMonitor", "TaskbarPlacement", "TaskbarOffsetPx",
            "TaskbarLeftOffsetPx", "TaskbarVerticalOffsetPx",
            "TaskbarFontFamily", "TaskbarFontSize", "TaskbarFontBold", "TaskbarBlockSpacing", "TaskbarStableWidth",
            "TaskbarLabelColor", "TaskbarValueColor", "TaskbarNetworkArrows", "TaskbarNetworkUnit",
            "TaskbarSwapNetworkLines", "TaskbarCompactLabels", "TaskbarRefreshSeconds", "TaskbarBatteryLayout",
            "TaskbarPrayerDisplay", "TaskbarPrayerApproachMinutes", "TaskbarPrayerElapsedMinutes", "TaskbarWeatherIcon",
            "ShowDeviceBatteriesOnTaskbar", "ShowDeviceBatteryBackground",
        })
            Assert.Contains(key, SettingsTransfer.LocalOnlyKeys);

        var settings = new JsonObject
        {
            ["TaskbarShowUpload"] = true,
            ["TaskbarShowNetwork"] = true,
            ["TaskbarFontFamily"] = "Consolas",
            ["CapsuleTheme"] = "Matcha",
        };
        var exported = SettingsTransfer.BuildExport(settings, new JsonArray())["settings"] as JsonObject;
        Assert.False(exported!.ContainsKey("TaskbarShowUpload"));
        Assert.False(exported.ContainsKey("TaskbarShowNetwork"));
        Assert.False(exported.ContainsKey("TaskbarFontFamily"));
        Assert.True(exported.ContainsKey("CapsuleTheme"));

        // An import never switches the readouts on a PC that has them off (and strips legacy keys).
        var merged = SettingsTransfer.Merge(
            new JsonObject { ["TaskbarShowUpload"] = true, ["TaskbarShowSystem"] = true },
            new JsonObject { ["TaskbarShowUpload"] = false, ["TaskbarItemsMigrated"] = true });
        Assert.False(merged["TaskbarShowUpload"]!.GetValue<bool>());
        Assert.False(merged.ContainsKey("TaskbarShowSystem"));
        Assert.False(SettingsTransfer.Merge(new JsonObject { ["TaskbarShowUpload"] = true }, new JsonObject()).ContainsKey("TaskbarShowUpload"));
    }

    [Fact]
    public void OldSettingsMigrateCoarseTaskbarSwitchesAndDropLegacyKeys()
    {
        var oldJson = """
            {
              "CapsuleTheme": "Matcha",
              "ShowDeviceBatteriesOnTaskbar": true,
              "TaskbarShowSystem": true,
              "TaskbarShowNetwork": true,
              "TaskbarShowPrayer": true
            }
            """;
        var settings = JsonSerializer.Deserialize<AppSettings>(oldJson)!;
        settings.MigrateTaskbarItems();

        Assert.True(settings.TaskbarItemsMigrated);
        Assert.True(settings.TaskbarShowCpuLoad);
        Assert.True(settings.TaskbarShowCpuTemp);
        Assert.True(settings.TaskbarShowGpuLoad);
        Assert.True(settings.TaskbarShowGpuTemp);
        Assert.False(settings.TaskbarShowMemory);
        Assert.False(settings.TaskbarShowDisk);
        Assert.True(settings.TaskbarShowUpload);
        Assert.True(settings.TaskbarShowDownload);
        Assert.True(settings.TaskbarShowPrayer);
        Assert.False(settings.TaskbarShowWeatherTemp);
        Assert.False(settings.TaskbarShowWeatherWind);
        Assert.True(settings.ShowDeviceBatteriesOnTaskbar);
        Assert.False(settings.ExtensionData?.ContainsKey("TaskbarShowSystem") ?? false);
        Assert.False(settings.ExtensionData?.ContainsKey("TaskbarShowNetwork") ?? false);

        // Idempotent: turning a migrated item off stays off if MigrateTaskbarItems runs again.
        settings.TaskbarShowCpuLoad = false;
        settings.MigrateTaskbarItems();
        Assert.False(settings.TaskbarShowCpuLoad);

        var blank = JsonSerializer.Deserialize<AppSettings>("{\"CapsuleTheme\":\"Matcha\"}")!;
        blank.MigrateTaskbarItems();
        Assert.False(blank.TaskbarShowCpuLoad);
        Assert.False(blank.TaskbarShowGpuLoad);
        Assert.False(blank.TaskbarShowUpload);
    }

    [Fact]
    public void TheTaskbarSectionIsInTheSettingsWindowAndHasAnAlias()
    {
        Assert.Equal("Taskbar", SettingsSections.Find(SettingsSections.Taskbar)!.Title);
        Assert.Equal(SettingsSections.Taskbar, SettingsSections.Resolve("taskbar-readouts"));
        Assert.Equal(SettingsSections.Taskbar, SettingsSections.Resolve("TASKBAR"));
    }

    [Fact]
    public void NeedsSamplerOnlyForSampledItems()
    {
        Assert.False(new TaskbarOptions { Prayer = true, WeatherTemp = true, WeatherWind = true, Batteries = true }.NeedsSampler);
        Assert.Equal(TaskbarNeeds.None, new TaskbarOptions { Prayer = true, WeatherTemp = true, Batteries = true }.Needs);
        Assert.True(new TaskbarOptions { CpuLoad = true }.NeedsSampler);
        Assert.True(new TaskbarOptions { GpuLoad = true }.NeedsSampler);
        Assert.True(new TaskbarOptions { GpuTemp = true }.NeedsSampler);
        Assert.True(new TaskbarOptions { Memory = true }.NeedsSampler);
        Assert.True(new TaskbarOptions { Disk = true }.NeedsSampler);
        Assert.True(new TaskbarOptions { Upload = true }.NeedsSampler);
        Assert.True(new TaskbarOptions { Download = true }.NeedsSampler);
    }

    [Fact]
    public void ComputeTooltipPositionStaysInsideTaskbarMonitorWhenSecondScreenIsBelow()
    {
        // Top monitor [0,0..1920,1080] with bottom taskbar [1032..1080]; 2nd monitor sits below at Y >= 1080.
        var (x, y) = TaskbarLayout.ComputeTooltipPosition(
            anchorX: 1600,
            tipWidth: 240,
            tipHeight: 64,
            taskbarTop: 1032,
            taskbarBottom: 1080,
            monitorLeft: 0,
            monitorTop: 0,
            monitorRight: 1920,
            monitorBottom: 1080,
            gapPx: 6);

        Assert.Equal(1480, x);
        Assert.Equal(962, y);
        Assert.True(y + 64 <= 1032);
        Assert.True(y + 64 < 1080); // never spills onto the bottom screen
    }

    [Fact]
    public void ComputeTooltipPositionClampsHorizontallyAndSupportsTopTaskbar()
    {
        // Right edge clamping inside [0..1920] so it does not cross onto a right-side monitor.
        var (rx, _) = TaskbarLayout.ComputeTooltipPosition(
            anchorX: 1900,
            tipWidth: 240,
            tipHeight: 64,
            taskbarTop: 1032,
            taskbarBottom: 1080,
            monitorLeft: 0,
            monitorTop: 0,
            monitorRight: 1920,
            monitorBottom: 1080,
            gapPx: 6);
        Assert.Equal(1674, rx);

        // Left edge clamping on a left-side negative-coordinate monitor [-1920,0..0,1080].
        var (lx, _) = TaskbarLayout.ComputeTooltipPosition(
            anchorX: -1900,
            tipWidth: 240,
            tipHeight: 64,
            taskbarTop: 1032,
            taskbarBottom: 1080,
            monitorLeft: -1920,
            monitorTop: 0,
            monitorRight: 0,
            monitorBottom: 1080,
            gapPx: 6);
        Assert.Equal(-1914, lx);

        // Top-edge taskbar [0..48] opens the tooltip below the taskbar, inside the monitor.
        var (tx, ty) = TaskbarLayout.ComputeTooltipPosition(
            anchorX: 500,
            tipWidth: 200,
            tipHeight: 50,
            taskbarTop: 0,
            taskbarBottom: 48,
            monitorLeft: 0,
            monitorTop: 0,
            monitorRight: 1920,
            monitorBottom: 1080,
            gapPx: 6);
        Assert.Equal(400, tx);
        Assert.Equal(54, ty);
    }

    [Fact]
    public void BuildHoverInfoFormatsTitleIconLinesAndWarning()
    {
        var blocks = new[]
        {
            new TaskbarBlock(TaskbarBlockKind.System, "CPU 12%", "GPU 4%", TopWarn: true, BottomWarn: false, "CPU 12%, 62°\r\nGPU 4%, 44°"),
            new TaskbarBlock(TaskbarBlockKind.Weather, "18°C", "", TopWarn: false, BottomWarn: false, "Clear, 18°C"),
        };

        var sys = TaskbarReadouts.BuildHoverInfo(TaskbarBlockKind.System, blocks);
        Assert.NotNull(sys);
        Assert.Equal("CPU & GPU", sys!.Title);
        Assert.Equal("NavGauge", sys.IconKey);
        Assert.Equal(new[] { "CPU 12%, 62°", "GPU 4%, 44°" }, sys.Lines);
        Assert.True(sys.HasWarning);

        var weather = TaskbarReadouts.BuildHoverInfo(TaskbarBlockKind.Weather, blocks);
        Assert.NotNull(weather);
        Assert.Equal("Weather", weather!.Title);
        Assert.Equal("NavWeather", weather.IconKey);
        Assert.Equal(new[] { "Clear, 18°C" }, weather.Lines);
        Assert.False(weather.HasWarning);

        Assert.Null(TaskbarReadouts.BuildHoverInfo(TaskbarBlockKind.Network, blocks));
    }
}

public class TaskbarSamplingTests
{
    [Fact]
    public void AggregateGpuEngineSumsValidValuesAndClamps()
    {
        Assert.Null(TaskbarSampling.AggregateGpuEngine(Array.Empty<double>()));
        Assert.Null(TaskbarSampling.AggregateGpuEngine(new[] { -1.0, double.NaN, double.PositiveInfinity }));
        Assert.Equal(28.5, TaskbarSampling.AggregateGpuEngine(new[] { 12.0, 16.5, -2.0 })!.Value, 6);
        Assert.Equal(100.0, TaskbarSampling.AggregateGpuEngine(new[] { 65.0, 60.0 })!.Value, 6);
    }

    [Fact]
    public void PickGpuLoadPrefersNvmlOtherwiseUsesBusiestPdhEngine()
    {
        Assert.Equal(42.0, TaskbarSampling.PickGpuLoad(42.0, 10.0, 80.0)!.Value, 6);
        Assert.Equal(73.0, TaskbarSampling.PickGpuLoad(null, 12.0, 73.0, 4.0, null)!.Value, 6);
        Assert.Equal(100.0, TaskbarSampling.PickGpuLoad(null, 150.0, 10.0)!.Value, 6);
        Assert.Null(TaskbarSampling.PickGpuLoad(null, null, double.NaN));
    }

    [Fact]
    public void PickGpuAndCpuTemperaturesSelectMeasuredAndFallbackCleanly()
    {
        Assert.Equal(61.0, TaskbarSampling.PickGpuTemperature(61.0, 58.0)!.Value, 6);
        Assert.Equal(58.0, TaskbarSampling.PickGpuTemperature(null, 58.0)!.Value, 6);
        Assert.Equal(58.0, TaskbarSampling.PickGpuTemperature(0.0, 58.0)!.Value, 6);
        Assert.Null(TaskbarSampling.PickGpuTemperature(null, 140.0));
        Assert.Null(TaskbarSampling.PickGpuTemperature(null, null));

        var measured = TaskbarSampling.PickCpuTemperature(67.5, 10.0);
        Assert.True(measured.Measured);
        Assert.Equal(67.5, measured.TempC!.Value, 6);

        var estimated = TaskbarSampling.PickCpuTemperature(null, 50.0);
        Assert.False(estimated.Measured);
        Assert.Equal(TaskbarSampling.EstimateCpuTemperature(50.0), estimated.TempC!.Value, 6);

        var none = TaskbarSampling.PickCpuTemperature(0.0, null);
        Assert.Null(none.TempC);
        Assert.False(none.Measured);
    }

    [Fact]
    public void CpuLoadFromTwoReadings()
    {
        var cpu = new CpuLoadDelta();
        Assert.Null(cpu.Update(1000, 2000));
        Assert.Equal(50, cpu.Update(1500, 3000)!.Value, 6);
        Assert.Equal(0, cpu.Update(2500, 4000)!.Value, 6);
        Assert.Equal(100, cpu.Update(2500, 5000)!.Value, 6);
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

    [Fact]
    public void TwoSamplersKeepTheirOwnDeltas()
    {
        var history = new CpuLoadDelta();
        var taskbar = new CpuLoadDelta();
        history.Update(0, 0);
        taskbar.Update(0, 0);
        for (ulong t = 1; t <= 15; t++) Assert.NotNull(taskbar.Update(t * 100, t * 1000));
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
        Assert.Null(net.Update(11, 100, 100));
        Assert.Null(net.Update(11, 200, 200));
        Assert.NotNull(net.Update(12, 300, 300));
        net.Reset();
        Assert.Null(net.Update(13, 400, 400));
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(80, 20)]
    [InlineData(0, 100)]
    [InlineData(130, 0)]
    [InlineData(-5, 100)]
    public void DiskActivityIsTheIdleTimeInverted(double idle, double activity) => Assert.Equal(activity, TaskbarSampling.DiskActivityFromIdle(idle));

    [Fact]
    public void TheCpuTemperatureEstimateGrowsWithLoad()
    {
        Assert.True(TaskbarSampling.EstimateCpuTemperature(0) < TaskbarSampling.EstimateCpuTemperature(100));
        Assert.Equal(42, TaskbarSampling.EstimateCpuTemperature(0));
    }
}
