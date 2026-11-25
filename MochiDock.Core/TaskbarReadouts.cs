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

/// <summary>Structured content for the themed taskbar hover card.</summary>
public sealed record TaskbarHoverInfo(string Title, string IconKey, IReadOnlyList<string> Lines, bool HasWarning);

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
    public TimeSpan? PrayerRemaining { get; init; }
    public string CurrentPrayerName { get; init; } = "";
    public string CurrentPrayerTime { get; init; } = "";
    public TimeSpan? CurrentPrayerElapsed { get; init; }
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
    public IReadOnlyDictionary<TaskbarBlockKind, TaskbarZone>? BlockZones { get; init; }
    public TaskbarArrowStyle NetworkArrows { get; init; } = TaskbarArrowStyle.Arrow;
    public TaskbarNetworkUnit NetworkUnit { get; init; } = TaskbarNetworkUnit.Bytes;
    public bool SwapNetworkLines { get; init; }
    public bool CompactLabels { get; init; }
    public TaskbarBatteryLayout BatteryLayout { get; init; } = TaskbarBatteryLayout.Auto;
    public TaskbarPrayerDisplay PrayerDisplay { get; init; } = TaskbarPrayerDisplay.TimeAndRemaining;
    public TaskbarWeatherIconStyle WeatherIconStyle { get; init; } = TaskbarWeatherIconStyle.Emoji;
    public int PrayerWarnMinutes { get; init; } = 10;
    public int PrayerElapsedMinutes { get; init; } = 15;

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

    public static TaskbarZone ZoneOf(IReadOnlyDictionary<TaskbarBlockKind, TaskbarZone>? zones, TaskbarBlockKind kind) =>
        zones != null && zones.TryGetValue(kind, out var z) ? z : TaskbarZone.Right;

    public static Dictionary<TaskbarBlockKind, TaskbarZone> ParseZones(IReadOnlyDictionary<string, string>? raw)
    {
        var map = new Dictionary<TaskbarBlockKind, TaskbarZone>();
        if (raw == null) return map;
        foreach (var kv in raw)
        {
            if (Enum.TryParse<TaskbarBlockKind>(kv.Key?.Trim(), ignoreCase: true, out var kind) && Enum.IsDefined(kind))
                map[kind] = TaskbarStyle.ParseZone(kv.Value);
        }
        return map;
    }

    /// <summary>The blocks to draw in the configured order; switched-off or unavailable items are skipped.</summary>
    public static IReadOnlyList<TaskbarBlock> Build(TaskbarSnapshot snapshot, TaskbarOptions options) =>
        BuildFiltered(snapshot, options, zoneFilter: null);

    /// <summary>The blocks assigned to a specific taskbar zone (<see cref="TaskbarZone.Left"/> or <see cref="TaskbarZone.Right"/>).</summary>
    public static IReadOnlyList<TaskbarBlock> BuildForZone(TaskbarSnapshot snapshot, TaskbarOptions options, TaskbarZone zone) =>
        BuildFiltered(snapshot, options, zone);

    /// <summary>Splits the readout into separate Left and Right zone lists in the configured order.</summary>
    public static (IReadOnlyList<TaskbarBlock> Left, IReadOnlyList<TaskbarBlock> Right) BuildSplit(TaskbarSnapshot snapshot, TaskbarOptions options) =>
        (BuildFiltered(snapshot, options, TaskbarZone.Left), BuildFiltered(snapshot, options, TaskbarZone.Right));

    /// <summary>Builds the themed hover card title, icon key, lines, and warning flag for a hovered taskbar block.</summary>
    public static TaskbarHoverInfo? BuildHoverInfo(TaskbarBlockKind kind, IReadOnlyList<TaskbarBlock> blocks)
    {
        var matching = blocks.Where(b => b.Kind == kind).ToList();
        if (matching.Count == 0) return null;

        var (title, iconKey) = kind switch
        {
            TaskbarBlockKind.System => ("CPU & GPU", "NavGauge"),
            TaskbarBlockKind.MemoryDisk => ("Memory & Disk", "NavGauge"),
            TaskbarBlockKind.Network => ("Network", "NavFx"),
            TaskbarBlockKind.Prayer => ("Prayer Times", "NavMoon"),
            TaskbarBlockKind.Weather => ("Weather", "NavWeather"),
            TaskbarBlockKind.Batteries => ("Device Batteries", "NavGauge"),
            _ => ("Taskbar", "NavGauge"),
        };

        var lines = new List<string>();
        foreach (var block in matching)
        {
            if (string.IsNullOrWhiteSpace(block.Tooltip)) continue;
            foreach (var raw in block.Tooltip.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                lines.Add(raw);
        }
        if (lines.Count == 0) return null;

        bool hasWarning = matching.Any(b => b.TopWarn || b.BottomWarn);
        return new TaskbarHoverInfo(title, iconKey, lines, hasWarning);
    }

    private static IReadOnlyList<TaskbarBlock> BuildFiltered(TaskbarSnapshot snapshot, TaskbarOptions options, TaskbarZone? zoneFilter)
    {
        var blocks = new List<TaskbarBlock>();
        foreach (var kind in NormalizeOrder(options.BlockOrder))
        {
            if (zoneFilter is { } target && ZoneOf(options.BlockZones, kind) != target) continue;
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
                    if (options.Prayer && PrayerBlock(snapshot, options) is { } prayer) blocks.Add(prayer);
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

    /// <summary>"13°C" for weather temperature.</summary>
    public static string WeatherTemperature(double celsius) => Temperature(celsius) + "C";

    /// <summary>
    /// Picks the taskbar weather emoji matching the dock's Weather tab: sun behind cloud for daytime Clear/PartlyCloudy,
    /// crescent moon for clear night skies, and standard weather icons for clouds/rain/snow/thunder.
    /// </summary>
    public static string ResolveWeatherGlyph(string? iconKind, string? fallbackGlyph, bool isNight) => iconKind switch
    {
        "Clear" => isNight ? "🌙" : "⛅",
        "PartlyCloudy" => isNight ? "☁️" : "⛅",
        "Cloudy" => "☁️",
        "Fog" => "🌫️",
        "Drizzle" => "🌦️",
        "Rain" => "🌧️",
        "Snow" => "🌨️",
        "Thunder" => "⛈️",
        _ => fallbackGlyph ?? "",
    };

    /// <summary>Extracts the Twemoji key for a weather symbol (accepting bare Unicode symbols like "☀" by retrying with U+FE0F).</summary>
    public static string WeatherEmojiKey(string? glyph)
    {
        if (string.IsNullOrWhiteSpace(glyph)) return "";
        foreach (var run in EmojiText.Split(glyph))
            if (run.IsEmoji && !string.IsNullOrEmpty(run.Key)) return run.Key;
        foreach (var run in EmojiText.Split(glyph.Trim() + "\uFE0F"))
            if (run.IsEmoji && !string.IsNullOrEmpty(run.Key)) return run.Key;
        return "";
    }

    /// <summary>"51 KB/s" or "1.2 MB/s": the same units as the Stats card (<see cref="RateScale.Format"/>).</summary>
    public static string Rate(double bytesPerSecond) => RateScale.Format(bytesPerSecond);

    /// <summary>Decimal bits per second ("408 Kb/s", "9.6 Mb/s", "1.2 Gb/s").</summary>
    public static string RateBits(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond <= 100) return "0 Kb/s";
        double bits = bytesPerSecond * 8.0;
        if (bits < 1_000_000)
        {
            double kb = bits / 1_000.0;
            return (kb >= 100 ? kb.ToString("0", CultureInfo.InvariantCulture) : kb.ToString("0.#", CultureInfo.InvariantCulture)) + " Kb/s";
        }
        if (bits < 1_000_000_000)
        {
            double mb = bits / 1_000_000.0;
            return (mb >= 100 ? mb.ToString("0", CultureInfo.InvariantCulture) : mb.ToString("0.0", CultureInfo.InvariantCulture)) + " Mb/s";
        }
        double gb = bits / 1_000_000_000.0;
        return gb.ToString("0.0", CultureInfo.InvariantCulture) + " Gb/s";
    }

    public static string Rate(double bytesPerSecond, TaskbarNetworkUnit unit) =>
        unit == TaskbarNetworkUnit.Bits ? RateBits(bytesPerSecond) : Rate(bytesPerSecond);

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
            cpuLabel = o.CompactLabels ? "C " : "CPU ";
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
            gpuLabel = o.CompactLabels ? "G " : "GPU ";
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
            topLabel = o.CompactLabels ? "M " : "MEM ";
            top = topLabel + Percent(r);
            topWarn = r >= o.MemoryWarn;
            tipParts.Add("Memory in use: " + Percent(r));
        }
        if (disk is { } d)
        {
            string diskLabel = o.CompactLabels ? "D " : "DISK ";
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
        string upTip = "", downTip = "";

        if (up is { } u)
        {
            string r = Rate(u, o.NetworkUnit);
            if (o.NetworkArrows == TaskbarArrowStyle.Emoji) { upEmoji = UploadEmojiKey; upText = r; }
            else { upLabel = o.NetworkArrows == TaskbarArrowStyle.Triangle ? "▲ " : "↑ "; upText = upLabel + r; }
            upTip = "Upload " + r;
        }
        if (down is { } d)
        {
            string r = Rate(d, o.NetworkUnit);
            if (o.NetworkArrows == TaskbarArrowStyle.Emoji) { downEmoji = DownloadEmojiKey; downText = r; }
            else { downLabel = o.NetworkArrows == TaskbarArrowStyle.Triangle ? "▼ " : "↓ "; downText = downLabel + r; }
            downTip = "Download " + r;
        }

        var first = o.SwapNetworkLines
            ? (Text: downText, Label: downLabel, Emoji: downEmoji, Tip: downTip)
            : (Text: upText, Label: upLabel, Emoji: upEmoji, Tip: upTip);
        var second = o.SwapNetworkLines
            ? (Text: upText, Label: upLabel, Emoji: upEmoji, Tip: upTip)
            : (Text: downText, Label: downLabel, Emoji: downEmoji, Tip: downTip);

        var tipParts = new List<string>(2);
        if (first.Tip.Length > 0) tipParts.Add(first.Tip);
        if (second.Tip.Length > 0) tipParts.Add(second.Tip);
        string tip = string.Join("\r\n", tipParts);

        if (first.Text.Length == 0)
        {
            return new TaskbarBlock(TaskbarBlockKind.Network, second.Text, "", false, false, tip)
            {
                TopLabel = second.Label,
                TopEmojiKey = second.Emoji,
            };
        }

        return new TaskbarBlock(TaskbarBlockKind.Network, first.Text, second.Text, false, false, tip)
        {
            TopLabel = first.Label,
            BottomLabel = second.Label,
            TopEmojiKey = first.Emoji,
            BottomEmojiKey = second.Emoji,
        };
    }

    /// <summary>"in 1h 25m", "in 12m", "&lt;1m" or "now".</summary>
    public static string FormatPrayerRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "now";
        if (remaining.TotalMinutes < 1) return "<1m";
        int hours = (int)remaining.TotalHours;
        int minutes = remaining.Minutes;
        return hours > 0 ? $"in {hours}h {minutes}m" : $"in {minutes}m";
    }

    /// <summary>"Now", "+5m", "+1h 5m".</summary>
    public static string FormatPrayerElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) return "now";
        if (elapsed.TotalMinutes < 1) return "Now";
        int hours = (int)elapsed.TotalHours;
        int minutes = elapsed.Minutes;
        return hours > 0 ? $"+{hours}h {minutes}m" : $"+{minutes}m";
    }

    private static TaskbarBlock? PrayerBlock(TaskbarSnapshot s, TaskbarOptions o)
    {
        string? rem = s.PrayerRemaining is { } span ? FormatPrayerRemaining(span) : null;
        int warnMinutes = TaskbarStyle.ClampPrayerWindowMinutes(o.PrayerWarnMinutes);
        int elapsedMinutes = TaskbarStyle.ClampPrayerWindowMinutes(o.PrayerElapsedMinutes);
        bool approaching = warnMinutes > 0 && s.PrayerRemaining is { } r && r > TimeSpan.Zero && r <= TimeSpan.FromMinutes(warnMinutes);
        bool showCurrent = !approaching
            && elapsedMinutes > 0
            && !string.IsNullOrWhiteSpace(s.CurrentPrayerName)
            && s.CurrentPrayerElapsed is { } el
            && el >= TimeSpan.Zero
            && el <= TimeSpan.FromMinutes(elapsedMinutes);

        if (showCurrent)
        {
            string elapsedText = FormatPrayerElapsed(s.CurrentPrayerElapsed!.Value);
            string tip = string.IsNullOrWhiteSpace(s.CurrentPrayerTime)
                ? $"Current prayer: {s.CurrentPrayerName} ({elapsedText})"
                : $"Current prayer: {s.CurrentPrayerName} ({s.CurrentPrayerTime}, {elapsedText})";
            if (!string.IsNullOrWhiteSpace(s.PrayerName) && !string.IsNullOrWhiteSpace(s.PrayerTime))
            {
                tip += "\r\n" + (rem != null
                    ? $"Next prayer: {s.PrayerName} at {s.PrayerTime} ({rem})"
                    : $"Next prayer: {s.PrayerName} at {s.PrayerTime}");
            }

            if (o.PrayerDisplay == TaskbarPrayerDisplay.TimeAndRemaining && !string.IsNullOrWhiteSpace(s.CurrentPrayerTime))
            {
                string currentLabel = s.CurrentPrayerName + " ";
                return new TaskbarBlock(TaskbarBlockKind.Prayer, currentLabel + s.CurrentPrayerTime, elapsedText, false, false, tip)
                {
                    TopLabel = currentLabel,
                };
            }

            return new TaskbarBlock(TaskbarBlockKind.Prayer, s.CurrentPrayerName, elapsedText, false, false, tip)
            {
                TopLabel = s.CurrentPrayerName,
            };
        }

        if (string.IsNullOrWhiteSpace(s.PrayerName) || string.IsNullOrWhiteSpace(s.PrayerTime)) return null;
        string nextTip = rem != null
            ? $"Next prayer: {s.PrayerName} at {s.PrayerTime} ({rem})"
            : $"Next prayer: {s.PrayerName} at {s.PrayerTime}";

        if (rem is null || (o.PrayerDisplay == TaskbarPrayerDisplay.Time && !approaching))
        {
            return new TaskbarBlock(TaskbarBlockKind.Prayer, s.PrayerName, s.PrayerTime, false, false, nextTip)
            {
                TopLabel = s.PrayerName,
            };
        }

        if (o.PrayerDisplay == TaskbarPrayerDisplay.Remaining || o.PrayerDisplay == TaskbarPrayerDisplay.Time)
        {
            return new TaskbarBlock(TaskbarBlockKind.Prayer, s.PrayerName, rem, false, approaching, nextTip)
            {
                TopLabel = s.PrayerName,
            };
        }

        string topLabel = s.PrayerName + " ";
        return new TaskbarBlock(TaskbarBlockKind.Prayer, topLabel + s.PrayerTime, rem, false, approaching, nextTip)
        {
            TopLabel = topLabel,
        };
    }

    private static TaskbarBlock? WeatherBlock(TaskbarSnapshot s, TaskbarOptions o)
    {
        double? temp = o.WeatherTemp ? s.WeatherTempC : null;
        double? wind = o.WeatherWind ? s.WindMetersPerSecond : null;
        if (temp is null && wind is null) return null;

        string top = "", bottom = "", topLabel = "", bottomLabel = "", topEmoji = "";
        if (temp is { } t)
        {
            string tempText = WeatherTemperature(t);
            string emojiKey = o.WeatherIconStyle == TaskbarWeatherIconStyle.Emoji ? WeatherEmojiKey(s.WeatherGlyph) : "";
            if (emojiKey.Length > 0)
            {
                topEmoji = emojiKey;
                top = tempText;
            }
            else
            {
                topLabel = s.WeatherGlyph.Length > 0 ? s.WeatherGlyph + " " : "";
                top = topLabel + tempText;
            }
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
            tipParts.Add((s.WeatherCondition.Length > 0 ? s.WeatherCondition + ", " : "") + WeatherTemperature(tt));
        else if (s.WeatherCondition.Length > 0)
            tipParts.Add(s.WeatherCondition);
        if (wind is { } ww)
            tipParts.Add("wind " + Wind(ww));

        return new TaskbarBlock(TaskbarBlockKind.Weather, top, bottom, false, false, string.Join(", ", tipParts))
        {
            TopLabel = topLabel,
            BottomLabel = bottomLabel,
            TopEmojiKey = topEmoji,
        };
    }

    /// <summary>The devices packed two per block in Auto/Stacked mode, or one per block side-by-side in Horizontal mode, at most <see cref="TaskbarOptions.MaxBatteries"/>.</summary>
    private static IEnumerable<TaskbarBlock> BatteryBlocks(TaskbarSnapshot s, TaskbarOptions o)
    {
        var devices = s.Batteries.Take(Math.Max(0, o.MaxBatteries)).ToArray();
        if (o.BatteryLayout == TaskbarBatteryLayout.Horizontal)
        {
            foreach (var d in devices)
            {
                yield return new TaskbarBlock(TaskbarBlockKind.Batteries,
                    BatteryText(d), "",
                    IsLow(d, o), false,
                    BatteryTooltip(d))
                {
                    TopGlyph = d.Glyph,
                    TopCharging = d.Charging,
                    TopDim = d.Stale,
                };
            }
            yield break;
        }

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
