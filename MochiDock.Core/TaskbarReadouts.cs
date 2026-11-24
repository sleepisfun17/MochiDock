using System.Globalization;

namespace MochiDock.Core;

/// <summary>The groups of the taskbar readout, in the fixed order they are drawn (left to right, before the tray).</summary>
public enum TaskbarBlockKind { System, MemoryDisk, Network, Prayer, Weather, Batteries }

/// <summary>
/// One two-line block of the taskbar readout (like Traffic Monitor): the top and bottom line, whether each is a warning (drawn in the warning
/// colour), and the tooltip. A block may have no bottom line (no GPU, one battery). Device batteries draw a glyph in front of their text
/// (from the Segoe icon font), a charging bolt and a dimmed colour while the reading is stale.
/// </summary>
public sealed record TaskbarBlock(TaskbarBlockKind Kind, string Top, string Bottom, bool TopWarn, bool BottomWarn, string Tooltip)
{
    public string TopGlyph { get; init; } = "";
    public string BottomGlyph { get; init; } = "";
    public bool TopCharging { get; init; }
    public bool BottomCharging { get; init; }
    public bool TopDim { get; init; }
    public bool BottomDim { get; init; }

    public bool HasBottom => Bottom.Length > 0;
}

/// <summary>One wireless device for the Batteries block. <see cref="Glyph"/> is the icon-font character the app draws for its kind.</summary>
public sealed record TaskbarBattery(string Glyph, string Name, string Via, int? Percent, bool Charging, bool Stale, DateTimeOffset ReadAt);

/// <summary>
/// Everything the readout can show, as plain values (null or empty means unavailable). The app fills it from its samplers; this file only decides
/// what to show. <see cref="CpuTempMeasured"/> false means the CPU temperature is the estimate (shown with "~", never a warning).
/// </summary>
public sealed record TaskbarSnapshot
{
    public double? CpuPercent { get; init; }
    public double? CpuTempC { get; init; }
    public bool CpuTempMeasured { get; init; }
    public double? GpuPercent { get; init; }
    public double? GpuTempC { get; init; }
    public double? RamPercent { get; init; }
    public double? DiskPercent { get; init; }
    public double? NetDownBytesPerSecond { get; init; }
    public double? NetUpBytesPerSecond { get; init; }
    public string PrayerName { get; init; } = "";
    public string PrayerTime { get; init; } = "";
    public double? WeatherTempC { get; init; }
    public double? WindMetersPerSecond { get; init; }
    public string WeatherGlyph { get; init; } = "";
    public string WeatherCondition { get; init; } = "";
    public IReadOnlyList<TaskbarBattery> Batteries { get; init; } = Array.Empty<TaskbarBattery>();
}

/// <summary>Which blocks are switched on, and the alert thresholds that turn a number into a warning.</summary>
public sealed record TaskbarOptions
{
    public bool System { get; init; }
    public bool MemoryDisk { get; init; }
    public bool Network { get; init; }
    public bool Prayer { get; init; }
    public bool Weather { get; init; }
    public bool Batteries { get; init; }
    /// <summary>CPU / GPU load at or over this is a warning (the existing alert settings).</summary>
    public double CpuAlert { get; init; } = 90;
    public double GpuAlert { get; init; } = 90;
    /// <summary>A measured temperature at or over this is a warning (an estimate never is).</summary>
    public double TempAlert { get; init; } = 85;
    /// <summary>Memory at or over this is a warning.</summary>
    public double MemoryWarn { get; init; } = 90;
    /// <summary>The most devices shown (two per block).</summary>
    public int MaxBatteries { get; init; } = TaskbarReadouts.MaxItems;
    /// <summary>A device at or under this and not charging is a warning.</summary>
    public int LowBattery { get; init; } = 15;

    public bool NeedsSampler => System || MemoryDisk || Network;
}

/// <summary>What the taskbar readout shows and how it is worded. Pure: no UI, no sampling.</summary>
public static class TaskbarReadouts
{
    /// <summary>The most wireless devices on the taskbar.</summary>
    public const int MaxItems = 4;

    /// <summary>The blocks to draw, in the fixed order System, MemoryDisk, Network, Prayer, Weather, Batteries; switched-off or unavailable ones are skipped.</summary>
    public static IReadOnlyList<TaskbarBlock> Build(TaskbarSnapshot snapshot, TaskbarOptions options)
    {
        var blocks = new List<TaskbarBlock>();
        if (options.System && SystemBlock(snapshot, options) is { } system) blocks.Add(system);
        if (options.MemoryDisk && MemoryDiskBlock(snapshot, options) is { } memory) blocks.Add(memory);
        if (options.Network && NetworkBlock(snapshot) is { } network) blocks.Add(network);
        if (options.Prayer && PrayerBlock(snapshot) is { } prayer) blocks.Add(prayer);
        if (options.Weather && WeatherBlock(snapshot) is { } weather) blocks.Add(weather);
        if (options.Batteries) blocks.AddRange(BatteryBlocks(snapshot, options));
        return blocks;
    }

    // ---- formatting --------------------------------------------------------------------------------------------------------------------

    /// <summary>"2%" (rounded to a whole number).</summary>
    public static string Percent(double value) => Math.Round(Math.Clamp(value, 0, 100), MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>"45°", or "~45°" for an estimate.</summary>
    public static string Temperature(double celsius, bool estimate = false) =>
        (estimate ? "~" : "") + Math.Round(celsius, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "°";

    /// <summary>"51 KB/s" or "1.2 MB/s": the same units as the Stats card (<see cref="RateScale.Format"/>).</summary>
    public static string Rate(double bytesPerSecond) => RateScale.Format(bytesPerSecond);

    /// <summary>"8 m/s" (whole metres per second).</summary>
    public static string Wind(double metersPerSecond) => Math.Round(metersPerSecond, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " m/s";

    // ---- the blocks ------------------------------------------------------------------------------------------------------------------

    private static TaskbarBlock? SystemBlock(TaskbarSnapshot s, TaskbarOptions o)
    {
        if (s.CpuPercent is not { } cpu) return null;
        string top = "CPU " + Percent(cpu);
        if (s.CpuTempC is { } cpuTemp) top += " " + Temperature(cpuTemp, estimate: !s.CpuTempMeasured);
        bool topWarn = cpu >= o.CpuAlert || (s.CpuTempMeasured && s.CpuTempC is { } measured && measured >= o.TempAlert);

        string bottom = "";
        bool bottomWarn = false;
        if (s.GpuPercent is { } gpu)
        {
            bottom = "GPU " + Percent(gpu);
            if (s.GpuTempC is { } gpuTemp) bottom += " " + Temperature(gpuTemp);
            bottomWarn = gpu >= o.GpuAlert || (s.GpuTempC is { } t && t >= o.TempAlert);
        }

        string tip = "CPU " + Percent(cpu);
        if (s.CpuTempC is { } ct) tip += s.CpuTempMeasured ? $", {Temperature(ct)}" : $", about {Temperature(ct)} (the CPU temperature is an estimate: no sensor reading)";
        if (s.GpuPercent is { } g) tip += $"\r\nGPU {Percent(g)}" + (s.GpuTempC is { } gt ? $", {Temperature(gt)}" : "");
        return new TaskbarBlock(TaskbarBlockKind.System, top, bottom, topWarn, bottomWarn, tip);
    }

    private static TaskbarBlock? MemoryDiskBlock(TaskbarSnapshot s, TaskbarOptions o)
    {
        if (s.RamPercent is not { } ram && s.DiskPercent is null) return null;
        string top = "", bottom = "";
        bool topWarn = false;
        string tip = "";
        if (s.RamPercent is { } r) { top = "MEM " + Percent(r); topWarn = r >= o.MemoryWarn; tip = "Memory in use: " + Percent(r); }
        if (s.DiskPercent is { } d)
        {
            string disk = "DISK " + Percent(d);
            if (top.Length == 0) top = disk; else bottom = disk;
            tip += (tip.Length > 0 ? "\r\n" : "") + "Disk activity: " + Percent(d);
        }
        return new TaskbarBlock(TaskbarBlockKind.MemoryDisk, top, bottom, topWarn, false, tip);
    }

    private static TaskbarBlock? NetworkBlock(TaskbarSnapshot s)
    {
        if (s.NetUpBytesPerSecond is not { } up || s.NetDownBytesPerSecond is not { } down) return null;
        return new TaskbarBlock(TaskbarBlockKind.Network, "↑ " + Rate(up), "↓ " + Rate(down), false, false,
            $"Upload {Rate(up)}\r\nDownload {Rate(down)}");
    }

    private static TaskbarBlock? PrayerBlock(TaskbarSnapshot s)
    {
        if (string.IsNullOrWhiteSpace(s.PrayerName) || string.IsNullOrWhiteSpace(s.PrayerTime)) return null;
        return new TaskbarBlock(TaskbarBlockKind.Prayer, s.PrayerName, s.PrayerTime, false, false, $"Next prayer: {s.PrayerName} at {s.PrayerTime}");
    }

    private static TaskbarBlock? WeatherBlock(TaskbarSnapshot s)
    {
        if (s.WeatherTempC is not { } temp) return null;
        string top = (s.WeatherGlyph.Length > 0 ? s.WeatherGlyph + " " : "") + Temperature(temp);
        string bottom = s.WindMetersPerSecond is { } wind ? "↘ " + Wind(wind) : "";
        string tip = (s.WeatherCondition.Length > 0 ? s.WeatherCondition + ", " : "") + Temperature(temp) + (s.WindMetersPerSecond is { } w ? ", wind " + Wind(w) : "");
        return new TaskbarBlock(TaskbarBlockKind.Weather, top, bottom, false, false, tip);
    }

    /// <summary>The devices packed two per block (the second one on the bottom line), at most <see cref="TaskbarOptions.MaxBatteries"/> in all.</summary>
    private static IEnumerable<TaskbarBlock> BatteryBlocks(TaskbarSnapshot s, TaskbarOptions o)
    {
        var devices = s.Batteries.Take(Math.Max(0, o.MaxBatteries)).ToArray();
        for (int i = 0; i < devices.Length; i += 2)
        {
            var first = devices[i];
            var second = i + 1 < devices.Length ? devices[i + 1] : null;
            yield return new TaskbarBlock(TaskbarBlockKind.Batteries,
                BatteryText(first), second is null ? "" : BatteryText(second),
                IsLow(first, o), second is not null && IsLow(second, o),
                BatteryTooltip(first) + (second is null ? "" : "\r\n" + BatteryTooltip(second)))
            {
                TopGlyph = first.Glyph, BottomGlyph = second?.Glyph ?? "",
                TopCharging = first.Charging, BottomCharging = second?.Charging ?? false,
                TopDim = first.Stale, BottomDim = second?.Stale ?? false,
            };
        }
    }

    private static string BatteryText(TaskbarBattery b) => b.Percent is { } p ? p.ToString(CultureInfo.InvariantCulture) + "%" : "?";
    private static bool IsLow(TaskbarBattery b, TaskbarOptions o) => b.Percent is { } p && p <= o.LowBattery && !b.Charging;

    private static string BatteryTooltip(TaskbarBattery b)
    {
        string text = $"{b.Name}: {(b.Percent is { } p ? p + "%" : "unknown")} ({b.Via})";
        if (b.Charging) text += ", charging";
        return text + (b.Stale ? ", last seen " : ", read ") + b.ReadAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}
