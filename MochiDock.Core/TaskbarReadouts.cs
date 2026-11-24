using System.Globalization;

namespace MochiDock.Core;

/// <summary>The groups of the taskbar readout, in the order chosen by the user.</summary>
public enum TaskbarBlockKind { System, MemoryDisk, Network, Prayer, Weather, Batteries }

/// <summary>
/// One two-line block of the taskbar readout (like Traffic Monitor): the top and bottom line, whether each is a warning (drawn in the warning
/// colour), and the tooltip. A block may have no bottom line (no GPU, one battery, or a single sub-item enabled).
/// Device batteries draw a glyph in front of their text (from the Segoe icon font), a charging bolt and a dimmed colour while the reading is stale.
/// </summary>
public sealed record TaskbarBlock(TaskbarBlockKind Kind, string Top, string Bottom, bool TopWarn, bool BottomWarn, string Tooltip)
{
    public string TopGlyph { get; init; } = "";
    public string BottomGlyph { get; init; } = "";
    public bool TopCharging { get; init; }
    public bool BottomCharging { get; init; }
    public bool TopDim { get; init; }
    public bool BottomDim { get; init; }
    public string TopLabel { get; init; } = "";
    public string BottomLabel { get; init; } = "";
    public string TopEmojiKey { get; init; } = "";
    public string BottomEmojiKey { get; init; } = "";

    public bool HasBottom => Bottom.Length > 0;

    public string TopValue => TopLabel.Length > 0 && Top.StartsWith(TopLabel, StringComparison.Ordinal) ? Top[TopLabel.Length..] : Top;
    public string BottomValue => BottomLabel.Length > 0 && Bottom.StartsWith(BottomLabel, StringComparison.Ordinal) ? Bottom[BottomLabel.Length..] : Bottom;
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

/// <summary>Which individual items are switched on, their block order, arrow style, and alert thresholds.</summary>
public sealed record TaskbarOptions
{
    public bool CpuLoad { get; init; }
    public bool CpuTemp { get; init; }
    public bool GpuLoad { get; init; }
    public bool GpuTemp { get; init; }
    public bool Memory { get; init; }
    public bool Disk { get; init; }
    public bool Upload { get; init; }
    public bool Download { get; init; }
    public bool Prayer { get; init; }
    public bool WeatherTemp { get; init; }
    public bool WeatherWind { get; init; }
    public bool Batteries { get; init; }

    public IReadOnlyList<TaskbarBlockKind>? BlockOrder { get; init; }
    public TaskbarArrowStyle NetworkArrows { get; init; } = TaskbarArrowStyle.Arrow;

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

    public TaskbarNeeds Needs =>
        (CpuLoad ? TaskbarNeeds.CpuLoad : 0)
        | (CpuTemp ? TaskbarNeeds.CpuTemp : 0)
        | (GpuLoad ? TaskbarNeeds.GpuLoad : 0)
        | (GpuTemp ? TaskbarNeeds.GpuTemp : 0)
        | (Memory ? TaskbarNeeds.Memory : 0)
        | (Disk ? TaskbarNeeds.Disk : 0)
        | (Upload || Download ? TaskbarNeeds.Network : 0);

    public bool NeedsSampler => Needs != TaskbarNeeds.None;
}

/// <summary>What the taskbar readout shows and how it is worded. Pure: no UI, no sampling.</summary>
public static class TaskbarReadouts
{
    /// <summary>The most wireless devices on the taskbar.</summary>
    public const int MaxItems = 4;

    public const string UploadEmojiKey = "1f53c";
    public const string DownloadEmojiKey = "1f53d";

    public static IReadOnlyList<TaskbarBlockKind> DefaultOrder { get; } = new[]
    {
        TaskbarBlockKind.System,
        TaskbarBlockKind.MemoryDisk,
        TaskbarBlockKind.Network,
        TaskbarBlockKind.Prayer,
        TaskbarBlockKind.Weather,
        TaskbarBlockKind.Batteries,
    };

    public static IReadOnlyList<string> DefaultOrderNames { get; } = DefaultOrder.Select(k => k.ToString()).ToArray();

    /// <summary>Deduplicates valid block names and appends any missing kinds in default order.</summary>
    public static IReadOnlyList<TaskbarBlockKind> NormalizeOrder(IEnumerable<string>? names)
    {
        var result = new List<TaskbarBlockKind>(DefaultOrder.Count);
        var seen = new HashSet<TaskbarBlockKind>();
        if (names != null)
        {
            foreach (var raw in names)
            {
                if (Enum.TryParse<TaskbarBlockKind>(raw?.Trim(), ignoreCase: true, out var kind)
                    && Enum.IsDefined(kind) && seen.Add(kind))
                {
                    result.Add(kind);
                }
            }
        }
        foreach (var kind in DefaultOrder)
            if (seen.Add(kind)) result.Add(kind);
        return result;
    }

    /// <summary>Deduplicates block kinds and appends any missing kinds in default order.</summary>
    public static IReadOnlyList<TaskbarBlockKind> NormalizeOrder(IEnumerable<TaskbarBlockKind>? kinds)
    {
        var result = new List<TaskbarBlockKind>(DefaultOrder.Count);
        var seen = new HashSet<TaskbarBlockKind>();
        if (kinds != null)
        {
            foreach (var kind in kinds)
                if (Enum.IsDefined(kind) && seen.Add(kind)) result.Add(kind);
        }
        foreach (var kind in DefaultOrder)
            if (seen.Add(kind)) result.Add(kind);
        return result;
    }

    /// <summary>Moves <paramref name="kind"/> by <paramref name="delta"/> (-1 up, +1 down), clamped to bounds.</summary>
    public static IReadOnlyList<TaskbarBlockKind> MoveBlock(IReadOnlyList<TaskbarBlockKind> order, TaskbarBlockKind kind, int delta)
    {
        var list = NormalizeOrder(order).ToList();
        int idx = list.IndexOf(kind);
        if (idx < 0 || delta == 0) return list;
        int target = Math.Clamp(idx + delta, 0, list.Count - 1);
        if (target == idx) return list;
        list.RemoveAt(idx);
        list.Insert(target, kind);
        return list;
    }

    /// <summary>The blocks to draw in the configured order; switched-off or unavailable items are skipped.</summary>
    public static IReadOnlyList<TaskbarBlock> Build(TaskbarSnapshot snapshot, TaskbarOptions options)
    {
        var blocks = new List<TaskbarBlock>();
        foreach (var kind in NormalizeOrder(options.BlockOrder))
        {
            switch (kind)
            {
                case TaskbarBlockKind.System:
                    if (SystemBlock(snapshot, options) is { } system) blocks.Add(system);
                    break;
                case TaskbarBlockKind.MemoryDisk:
                    if (MemoryDiskBlock(snapshot, options) is { } memory) blocks.Add(memory);
                    break;
                case TaskbarBlockKind.Network:
                    if (NetworkBlock(snapshot, options) is { } network) blocks.Add(network);
                    break;
                case TaskbarBlockKind.Prayer:
                    if (options.Prayer && PrayerBlock(snapshot) is { } prayer) blocks.Add(prayer);
                    break;
                case TaskbarBlockKind.Weather:
                    if (WeatherBlock(snapshot, options) is { } weather) blocks.Add(weather);
                    break;
                case TaskbarBlockKind.Batteries:
                    if (options.Batteries) blocks.AddRange(BatteryBlocks(snapshot, options));
                    break;
            }
        }
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
        double? cpu = o.CpuLoad ? s.CpuPercent : null;
        double? cpuTemp = o.CpuTemp ? s.CpuTempC : null;
        double? gpu = o.GpuLoad ? s.GpuPercent : null;
        double? gpuTemp = o.GpuTemp ? s.GpuTempC : null;

        string cpuLine = "", cpuLabel = "";
        bool cpuWarn = false;
        if (cpu is not null || cpuTemp is not null)
        {
            cpuLabel = "CPU ";
            var parts = new List<string>(2);
            if (cpu is { } c) parts.Add(Percent(c));
            if (cpuTemp is { } ct) parts.Add(Temperature(ct, estimate: !s.CpuTempMeasured));
            cpuLine = cpuLabel + string.Join(" ", parts);
            cpuWarn = (cpu is { } cw && cw >= o.CpuAlert) || (cpuTemp is { } ctw && s.CpuTempMeasured && ctw >= o.TempAlert);
        }

        string gpuLine = "", gpuLabel = "";
        bool gpuWarn = false;
        if (gpu is not null || gpuTemp is not null)
        {
            gpuLabel = "GPU ";
            var parts = new List<string>(2);
            if (gpu is { } g) parts.Add(Percent(g));
            if (gpuTemp is { } gt) parts.Add(Temperature(gt));
            gpuLine = gpuLabel + string.Join(" ", parts);
            gpuWarn = (gpu is { } gw && gw >= o.GpuAlert) || (gpuTemp is { } gtw && gtw >= o.TempAlert);
        }

        if (cpuLine.Length == 0 && gpuLine.Length == 0) return null;

        var tipParts = new List<string>(2);
        if (cpuLine.Length > 0)
        {
            var cp = new List<string>(2);
            if (cpu is { } c) cp.Add(Percent(c));
            if (cpuTemp is { } ct) cp.Add(s.CpuTempMeasured ? Temperature(ct) : $"about {Temperature(ct)} (the CPU temperature is an estimate: no sensor reading)");
            tipParts.Add("CPU " + string.Join(", ", cp));
        }
        if (gpuLine.Length > 0)
        {
            var gp = new List<string>(2);
            if (gpu is { } g) gp.Add(Percent(g));
            if (gpuTemp is { } gt) gp.Add(Temperature(gt));
            tipParts.Add("GPU " + string.Join(", ", gp));
        }
        string tip = string.Join("\r\n", tipParts);

        if (cpuLine.Length == 0)
            return new TaskbarBlock(TaskbarBlockKind.System, gpuLine, "", gpuWarn, false, tip) { TopLabel = gpuLabel };

        return new TaskbarBlock(TaskbarBlockKind.System, cpuLine, gpuLine, cpuWarn, gpuWarn, tip)
        {
            TopLabel = cpuLabel,
            BottomLabel = gpuLabel,
        };
    }

    private static TaskbarBlock? MemoryDiskBlock(TaskbarSnapshot s, TaskbarOptions o)
    {
        double? ram = o.Memory ? s.RamPercent : null;
        double? disk = o.Disk ? s.DiskPercent : null;
        if (ram is null && disk is null) return null;

        string top = "", bottom = "", topLabel = "", bottomLabel = "";
        bool topWarn = false;
        var tipParts = new List<string>(2);
        if (ram is { } r)
        {
            topLabel = "MEM ";
            top = topLabel + Percent(r);
            topWarn = r >= o.MemoryWarn;
            tipParts.Add("Memory in use: " + Percent(r));
        }
        if (disk is { } d)
        {
            const string diskLabel = "DISK ";
            string diskText = diskLabel + Percent(d);
            if (top.Length == 0) { top = diskText; topLabel = diskLabel; }
            else { bottom = diskText; bottomLabel = diskLabel; }
            tipParts.Add("Disk activity: " + Percent(d));
        }
        return new TaskbarBlock(TaskbarBlockKind.MemoryDisk, top, bottom, topWarn, false, string.Join("\r\n", tipParts))
        {
            TopLabel = topLabel,
            BottomLabel = bottomLabel,
        };
    }

    private static TaskbarBlock? NetworkBlock(TaskbarSnapshot s, TaskbarOptions o)
    {
        double? up = o.Upload ? s.NetUpBytesPerSecond : null;
        double? down = o.Download ? s.NetDownBytesPerSecond : null;
        if (up is null && down is null) return null;

        string upText = "", upLabel = "", upEmoji = "";
        string downText = "", downLabel = "", downEmoji = "";
        var tipParts = new List<string>(2);

        if (up is { } u)
        {
            string r = Rate(u);
            if (o.NetworkArrows == TaskbarArrowStyle.Emoji)
            {
                upEmoji = UploadEmojiKey;
                upText = r;
            }
            else
            {
                upLabel = o.NetworkArrows == TaskbarArrowStyle.Triangle ? "▲ " : "↑ ";
                upText = upLabel + r;
            }
            tipParts.Add("Upload " + r);
        }
        if (down is { } d)
        {
            string r = Rate(d);
            if (o.NetworkArrows == TaskbarArrowStyle.Emoji)
            {
                downEmoji = DownloadEmojiKey;
                downText = r;
            }
            else
            {
                downLabel = o.NetworkArrows == TaskbarArrowStyle.Triangle ? "▼ " : "↓ ";
                downText = downLabel + r;
            }
            tipParts.Add("Download " + r);
        }

        string tip = string.Join("\r\n", tipParts);
        if (upText.Length == 0)
        {
            return new TaskbarBlock(TaskbarBlockKind.Network, downText, "", false, false, tip)
            {
                TopLabel = downLabel,
                TopEmojiKey = downEmoji,
            };
        }

        return new TaskbarBlock(TaskbarBlockKind.Network, upText, downText, false, false, tip)
        {
            TopLabel = upLabel,
            BottomLabel = downLabel,
            TopEmojiKey = upEmoji,
            BottomEmojiKey = downEmoji,
        };
    }

    private static TaskbarBlock? PrayerBlock(TaskbarSnapshot s)
    {
        if (string.IsNullOrWhiteSpace(s.PrayerName) || string.IsNullOrWhiteSpace(s.PrayerTime)) return null;
        return new TaskbarBlock(TaskbarBlockKind.Prayer, s.PrayerName, s.PrayerTime, false, false, $"Next prayer: {s.PrayerName} at {s.PrayerTime}")
        {
            TopLabel = s.PrayerName,
        };
    }

    private static TaskbarBlock? WeatherBlock(TaskbarSnapshot s, TaskbarOptions o)
    {
        double? temp = o.WeatherTemp ? s.WeatherTempC : null;
        double? wind = o.WeatherWind ? s.WindMetersPerSecond : null;
        if (temp is null && wind is null) return null;

        string top = "", bottom = "", topLabel = "", bottomLabel = "";
        if (temp is { } t)
        {
            topLabel = s.WeatherGlyph.Length > 0 ? s.WeatherGlyph + " " : "";
            top = topLabel + Temperature(t);
        }
        if (wind is { } w)
        {
            const string windLabel = "↘ ";
            string windText = windLabel + Wind(w);
            if (top.Length == 0) { top = windText; topLabel = windLabel; }
            else { bottom = windText; bottomLabel = windLabel; }
        }

        var tipParts = new List<string>(2);
        if (temp is { } tt)
            tipParts.Add((s.WeatherCondition.Length > 0 ? s.WeatherCondition + ", " : "") + Temperature(tt));
        else if (s.WeatherCondition.Length > 0)
            tipParts.Add(s.WeatherCondition);
        if (wind is { } ww)
            tipParts.Add("wind " + Wind(ww));

        return new TaskbarBlock(TaskbarBlockKind.Weather, top, bottom, false, false, string.Join(", ", tipParts))
        {
            TopLabel = topLabel,
            BottomLabel = bottomLabel,
        };
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
