using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using Microsoft.Win32;

namespace MochiDock.Services;

public class CoreUsageItem
{
    public int CoreIndex { get; set; }
    public double Usage { get; set; }
    public string CoreKind { get; set; } = "Any"; // "Performance", "Efficiency", "Any"
    public string ColorHex => CoreKind == "Performance" ? "#5E5CE6" : (CoreKind == "Efficiency" ? "#64D2FF" : "#0A84FF");
    public System.Windows.Media.Brush CoreBrush => CoreKind == "Performance" ? PerformanceBrush : (CoreKind == "Efficiency" ? EfficiencyBrush : AnyBrush);

    // One frozen brush per core kind, shared by every item and every redraw (the colours never change).
    private static readonly System.Windows.Media.Brush PerformanceBrush = FrozenBrush("#5E5CE6"), EfficiencyBrush = FrozenBrush("#64D2FF"), AnyBrush = FrozenBrush("#0A84FF");

    private static System.Windows.Media.Brush FrozenBrush(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
    public double HeightValue => Math.Max(2, Math.Min(46, (Usage / 100.0) * 46.0));
}

public class ProcessMetricItem
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public double? CpuPercent { get; set; }
    public string Description => Name.Contains("Compression", StringComparison.OrdinalIgnoreCase) ? "Windows compressed memory: keeps memory in RAM to reduce disk paging." : DisplayName;
    public string MemoryUsageText { get; set; } = "";
    public string FormattedDiskRate { get; set; } = "";
    public string ValueText { get; set; } = "";
    public ImageSource? Icon { get; set; }
}

public class DriveMetricItem
{
    public string Name { get; set; } = "";
    public string VolumeLabel { get; set; } = "";
    public double FreeGb { get; set; }
    public double TotalGb { get; set; }
    public double UsedPercent => TotalGb > 0 ? Math.Clamp(((TotalGb - FreeGb) / TotalGb) * 100.0, 0, 100) : 0;
    public string TitleText => string.IsNullOrWhiteSpace(VolumeLabel) ? Name : $"{Name} ({VolumeLabel})";
    public string FreeText => $"{FreeGb:0.#} GB free";
    public string TotalText => $"{TotalGb:0.#} GB";
    public string DisplayText => string.IsNullOrWhiteSpace(VolumeLabel) ? $"{Name}  {FreeText}" : $"{Name} ({VolumeLabel})  {FreeText}";
}

public class HardwareStats
{
    public MochiDock.Core.AlertReading? CpuUsageReading {get;set;}
    public MochiDock.Core.AlertReading? GpuUsageReading {get;set;}
    public MochiDock.Core.AlertReading? CpuTemperatureReading {get;set;}
    public MochiDock.Core.AlertReading? GpuTemperatureReading {get;set;}
    // CPU
    public float CpuUsage { get; set; }
    public float CpuTemp { get; set; }
    /// <summary>True only when a fresh sensor reading backs <see cref="CpuTemp"/>; otherwise it is an estimate from CPU load.</summary>
    public bool IsCpuTempMeasured => CpuTemperatureReading != null;
    public string CpuTempText => CpuTemp > 0 ? (IsCpuTempMeasured ? $"{CpuTemp:0}°" : $"~{CpuTemp:0}°") : "--";
    public string CpuTempTooltip => IsCpuTempMeasured ? "Measured by the CPU sensor." : "Estimated from CPU load. The optional PawnIO driver gives a real CPU temperature (Settings > Hardware & power > CPU sensors).";
    public string CpuName { get; set; } = "CPU";
    public double? CpuSpeedGhz { get; set; }
    public double? CpuBaseGhz { get; set; }
    public double CpuMaxGhz { get; set; } = 4.5;
    public string CpuSpeedGhzText => CpuSpeedGhz?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "—";
    public string CpuClockStatus { get; set; } = "Waiting for a CPU frequency sample.";
    public double CpuSpeedGhzPercent => CpuSpeedGhz.HasValue && CpuMaxGhz > 0 ? Math.Clamp(CpuSpeedGhz.Value / CpuMaxGhz * 100.0, 0, 100) : 0;
    public int LogicalCores { get; set; } = Environment.ProcessorCount;
    public double? SystemCpuPercent { get; set; }
    public double? UserCpuPercent { get; set; }
    public double? IdleCpuPercent { get; set; }
    public string SystemCpuPercentText => FormatCpuPercent(SystemCpuPercent);
    public string UserCpuPercentText => FormatCpuPercent(UserCpuPercent);
    public string IdleCpuPercentText => FormatCpuPercent(IdleCpuPercent);
    static string FormatCpuPercent(double? value) => value?.ToString("0", System.Globalization.CultureInfo.InvariantCulture) is { } text ? text + "%" : "—";
    public bool IsHybridCpu { get; set; }
    /// <summary>"8 P-cores (16 threads) · 16 E-cores" on a hybrid CPU, "16 cores · 32 threads" otherwise.</summary>
    public string CoreSummaryText { get; set; } = "";
    /// <summary>"PER CORE · 8P + 16E" on a hybrid CPU, "PER CORE / THREADS" otherwise (the full counts are in the tooltip).</summary>
    public string CoreHeaderText { get; set; } = "PER CORE / THREADS";
    public string CoreGridTooltip => IsHybridCpu
        ? $"{CoreSummaryText}\nTop rows: P-core threads (purple). Bottom rows: E-cores (light blue).\nEach cell fills with that thread's load."
        : $"{CoreSummaryText}\nEach cell fills with that thread's load.";
    public float EfficiencyCoresPercent { get; set; }
    public float PerformanceCoresPercent { get; set; }
    public string SystemUptimeText
    {
        get
        {
            var ts = TimeSpan.FromMilliseconds(Environment.TickCount64);
            if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h";
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m";
        }
    }

    // RAM
    public float RamUsagePercent { get; set; }
    public float UsedRamGb { get; set; }
    public float TotalRamGb { get; set; }
    public float FreeRamGb => Math.Max(0, TotalRamGb - UsedRamGb);
    public float FreeRamPercent => TotalRamGb > 0 ? Math.Clamp(FreeRamGb / TotalRamGb * 100f, 0, 100) : 0;
    public string RamUsedText => $"{UsedRamGb:0.0} GB";
    public string RamFreeText => $"{FreeRamGb:0.0} GB";
    public string RamPressureText => RamUsagePercent >= 90 ? "Critical" : (RamUsagePercent >= 75 ? "Warning" : "Normal");
    public string RamPressureTooltip => $"Memory pressure: {RamPressureText}\n• Normal: < 75% load\n• Warning: 75% – 89% load\n• Critical: >= 90% load (risk of disk paging)";
    public string RamAvailableGbText => $"{FreeRamGb:0.0} GB";
    // Committed memory and the file cache from GetPerformanceInfo; null when Windows did not report them.
    public float? CommittedRamGb { get; set; }
    public float? CommitLimitGb { get; set; }
    public float? CachedRamGb { get; set; }
    public string RamTotalText => TotalRamGb > 0 ? $"{TotalRamGb:0.0} GB" : "-";
    public string RamCommittedText => CommittedRamGb is { } c && CommitLimitGb is { } l ? $"{c:0.0} / {l:0} GB" : "-";
    public string RamCachedText => CachedRamGb is { } c ? $"{c:0.0} GB" : "-";

    // GPU & VRAM
    public float GpuUsage { get; set; }
    public float GpuTemp { get; set; }
    /// <summary>True only when a fresh sensor reading backs <see cref="GpuTemp"/>; otherwise the value is an estimate.</summary>
    public bool IsGpuTempMeasured => GpuTemperatureReading != null;
    public string GpuTempTooltip => IsGpuTempMeasured ? "Measured by the GPU sensor." : "Estimated from GPU load (no temperature sensor available).";
    public string GpuTempText => GpuTemp > 0 ? (IsGpuTempMeasured ? $"{GpuTemp:0}°" : $"~{GpuTemp:0}°") : "--";
    public float GpuEngine3DPercent { get; set; }
    public float GpuEngineCopyPercent { get; set; }
    public float GpuEngineDecodePercent { get; set; }
    public float GpuEngineEncodePercent { get; set; }
    public float UsedVramGb { get; set; }
    public float TotalVramGb { get; set; }
    public float FreeVramGb => Math.Max(0, TotalVramGb - UsedVramGb);
    public float VramUsagePercent => TotalVramGb > 0 ? Math.Clamp((UsedVramGb / TotalVramGb) * 100f, 0, 100) : 0;
    public string GpuName { get; set; } = "GPU";
    public string GpuDriverVersion { get; set; } = "–";
    public float GpuPowerWatts { get; set; } = 0f;
    public float GpuClockMhz { get; set; } = 0f;
    public string GpuPowerText => GpuPowerWatts > 0 ? $"{GpuPowerWatts:0} W" : "–";
    public string GpuClockText => GpuClockMhz > 0 ? $"{GpuClockMhz:0} MHz" : "–";
    public string VramUsedText => $"{UsedVramGb:0.0} GB";
    public string VramTotalText => $"{TotalVramGb:0.0} GB";
    public string VramDetailedSummaryText => TotalVramGb > 0 ? $"{UsedVramGb:0.0} / {TotalVramGb:0.0} GB" : $"{UsedVramGb:0.0} GB";

    // Drives
    public ObservableCollection<DriveMetricItem> DrivesList { get; set; } = new();
    public double DiskReadSpeedMbps { get; set; }
    public double DiskWriteSpeedMbps { get; set; }
    public double DiskReadBytesPerSec { get; set; }
    public double DiskWriteBytesPerSec { get; set; }
    public string FormattedDiskSpeed { get; set; } = "0 KB/s";
    public string DiskReadSpeedText => FormatRate(DiskReadBytesPerSec > 0 ? DiskReadBytesPerSec : DiskReadSpeedMbps * 1024 * 1024);
    public string DiskWriteSpeedText => FormatRate(DiskWriteBytesPerSec > 0 ? DiskWriteBytesPerSec : DiskWriteSpeedMbps * 1024 * 1024);
    public string DiskChartScaleText { get; set; } = "10 MB/s";

    // Network
    public long NetworkPingMs { get; set; } = -1;
    public string PingText => NetworkPingMs < 0 ? "—" : $"{NetworkPingMs} ms";
    public double DownloadSpeedMbps { get; set; }
    public double UploadSpeedMbps { get; set; }
    public double NetDownloadBytesPerSec { get; set; }
    public double NetUploadBytesPerSec { get; set; }
    public string FormattedDownloadSpeed => FormatRate(NetDownloadBytesPerSec > 0 ? NetDownloadBytesPerSec : DownloadSpeedMbps * 1024 * 1024);
    public string FormattedUploadSpeed => FormatRate(NetUploadBytesPerSec > 0 ? NetUploadBytesPerSec : UploadSpeedMbps * 1024 * 1024);
    public string NetChartScaleText { get; set; } = "200 KB/s";
    public string NetInterfaceName { get; set; } = "Ethernet / Wi-Fi";
    public string NetStatusText { get; set; } = "Connected";
    public string NetIpAddress { get; set; } = "192.168.1.1";
    public string NetMacAddress { get; set; } = "–";
    public string NetLinkSpeedText { get; set; } = "1.0 Gbps";
    public string NetSessionReceivedText { get; set; } = "0 MB";
    public string NetSessionSentText { get; set; } = "0 MB";
    public string NetPeakDownloadText { get; set; } = "0 KB/s";
    public string NetPeakUploadText { get; set; } = "0 KB/s";

    public static string FormatRate(double bytesPerSec) => MochiDock.Core.RateScale.Format(bytesPerSec);

    // Games
    public int CurrentFps { get; set; } = 0;
    public string ActiveGameName { get; set; } = "";
    public string ActiveGameTitle { get; set; } = "";
    public string DisplayGameTitle => !string.IsNullOrWhiteSpace(ActiveGameTitle) ? ActiveGameTitle : (!string.IsNullOrWhiteSpace(ActiveGameName) ? ActiveGameName : "No game in front");
    public string GameStatusText => !string.IsNullOrEmpty(ActiveGameName) ? (CurrentFps > 0 ? $"{CurrentFps} FPS" : "Running") : "Off";
    public double AverageFrameTimeMs { get; set; } = 0.0;
    public double SlowestFrameTimeMs { get; set; } = 0.0;
    public string AverageFrameTimeText => AverageFrameTimeMs > 0 ? $"{AverageFrameTimeMs:0.0} ms" : "–";
    public string SlowestFrameTimeText => SlowestFrameTimeMs > 0 ? $"{SlowestFrameTimeMs:0.0} ms" : "–";
    public float FpsCeiling { get; set; } = 144f;
    public string FpsCeilingText => $"{FpsCeiling:0}";

    // Stats are built on a thread-pool thread (MainViewModel's hardware poll) and handed to the UI.
    // An unfrozen PointCollection is owned by the thread that created it, and binding to it from the UI
    // throws "Must create DependencySource on same Thread as the DependencyObject". Every default
    // therefore shares this frozen, thread-free empty instance; real series come frozen from ToPointCollection.
    private static readonly PointCollection EmptyPoints = CreateEmptyPoints();

    private static PointCollection CreateEmptyPoints()
    {
        var points = new PointCollection();
        points.Freeze();
        return points;
    }

    // History Polyline & Area Polygon Points
    public PointCollection CpuHistoryPoints { get; set; } = EmptyPoints;
    public PointCollection CpuHistoryFillPoints { get; set; } = EmptyPoints;

    public PointCollection GpuHistoryPoints { get; set; } = EmptyPoints;
    public PointCollection GpuHistoryFillPoints { get; set; } = EmptyPoints;

    public PointCollection RamHistoryPoints { get; set; } = EmptyPoints;
    public PointCollection RamHistoryFillPoints { get; set; } = EmptyPoints;

    public PointCollection DiskHistoryPoints { get; set; } = EmptyPoints;
    public PointCollection DiskHistoryFillPoints { get; set; } = EmptyPoints;
    public PointCollection DiskWriteHistoryPoints { get; set; } = EmptyPoints;
    public PointCollection DiskWriteHistoryFillPoints { get; set; } = EmptyPoints;

    public PointCollection NetHistoryPoints { get; set; } = EmptyPoints;
    public PointCollection NetHistoryFillPoints { get; set; } = EmptyPoints;
    public PointCollection NetUploadHistoryPoints { get; set; } = EmptyPoints;
    public PointCollection NetUploadHistoryFillPoints { get; set; } = EmptyPoints;

    public PointCollection FpsHistoryPoints { get; set; } = EmptyPoints;
    public PointCollection FpsHistoryFillPoints { get; set; } = EmptyPoints;

    // Live frame-time chart (220 x 40): the slowest frame of each 250 ms over the last minute, with the stutter guide line.
    public const double FrameTimeChartWidth = 220, FrameTimeChartHeight = 40;
    public PointCollection FrameTimePoints { get; set; } = EmptyPoints;
    public double FrameTimeGuideY { get; set; } = FrameTimeChartHeight;
    public string FrameTimeCeilingText { get; set; } = "";
    public bool HasFrameTimeChart => FrameTimePoints.Count >= 2;

    // Top strip mini-sparklines
    public PointCollection CpuChipSparkPoints { get; set; } = EmptyPoints;
    public PointCollection GpuChipSparkPoints { get; set; } = EmptyPoints;
    public PointCollection DiskChipSparkPoints { get; set; } = EmptyPoints;
    public PointCollection NetChipSparkPoints { get; set; } = EmptyPoints;
    public PointCollection FpsChipSparkPoints { get; set; } = EmptyPoints;

    // Process & Core Collections
    public ObservableCollection<CoreUsageItem> CoreUsages { get; set; } = new();
    public ObservableCollection<ProcessMetricItem> TopProcesses { get; set; } = new();
    public ObservableCollection<ProcessMetricItem> TopMemoryProcesses { get; set; } = new();
    public ObservableCollection<ProcessMetricItem> TopDiskProcesses { get; set; } = new();

    // Active state flags
    public bool IsNvmlAvailable { get; set; }
    public bool IsAmdAvailable { get; set; }
    public bool IsGamingActive { get; set; }
    public bool IsGamingMode { get; set; } = true;
    public bool IsThermalWarning => (IsGpuTempMeasured && GpuTemp >= 80f) || (IsCpuTempMeasured && CpuTemp >= 85f);
}
