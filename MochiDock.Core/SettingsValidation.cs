using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>Pure validation and clamping rules for user-editable settings, shared by the UI and settings import.</summary>
public static class SettingsValidation
{
    /// <summary>Ports the app accepts for GOWA and its event listener: unprivileged ports only.</summary>
    public const int PortMin = 1024;
    public const int PortMax = 65535;

    public const double CpuGpuThresholdMin = 50, CpuGpuThresholdMax = 100;
    public const double TemperatureThresholdMin = 60, TemperatureThresholdMax = 100;
    public const double TempColorMin = 40, TempColorMax = 100;
    public const double DefaultCpuTempWarm = 75, DefaultCpuTempHot = 85;
    public const double DefaultGpuTempWarm = 70, DefaultGpuTempHot = 80;
    public const double GamingOffsetMin = 60, GamingOffsetMax = 260;
    public const double TopMarginMin = 0, TopMarginMax = 100;

    public static bool IsValidPort(int port) => port >= PortMin && port <= PortMax;

    /// <summary>The interface size in percent, limited to the range the settings offer (80 to 200).</summary>
    public static int ClampInterfaceScale(int percent) => InterfaceScale.Clamp(percent);

    public static bool TryParsePort(string? text, out int port) =>
        int.TryParse(text?.Trim(), out port) && IsValidPort(port);

    /// <summary>Trims whitespace and trailing slashes. Returns "" for blank input.</summary>
    public static string TrimRemoteUrl(string? url) => (url ?? "").Trim().TrimEnd('/');

    /// <summary>Like <see cref="TrimRemoteUrl"/>, and adds "http://" when no scheme was typed.</summary>
    public static string NormalizeRemoteUrl(string? url)
    {
        string trimmed = TrimRemoteUrl(url);
        return trimmed.Length == 0 || trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "http://" + trimmed;
    }

    /// <summary>True when the address should trigger the "not on a private network" warning (non-blank and public).</summary>
    public static bool ShouldWarnPublicAddress(string? url) =>
        !string.IsNullOrWhiteSpace(url) && !GowaRemotePolicy.IsPrivateAddress(url);

    /// <summary>Parses a time of day such as "22:00", "2200" or "22" (0:00 up to, not including, 24:00); see <see cref="TimeInput"/>.</summary>
    public static bool TryParseTimeOfDay(string? text, out TimeSpan time) => TimeInput.TryParse(text, out time);

    /// <summary>
    /// Quiet-hours test. A window that crosses midnight (start &gt; end) wraps; start == end is an empty window.
    /// Unparseable times mean "not quiet".
    /// </summary>
    public static bool IsQuietTime(TimeSpan now, string? start, string? end)
    {
        if (!TryParseTimeOfDay(start, out var s) || !TryParseTimeOfDay(end, out var e)) return false;
        return s <= e ? now >= s && now < e : now >= s || now < e;
    }

    /// <summary>
    /// Adds the folder names typed in <paramref name="input"/> (separated by comma, ideographic comma, semicolon or new line)
    /// to the tracked Outlook folders. Returns false with a message when nothing new could be added.
    /// </summary>
    public static bool TryAddOutlookFolders(IEnumerable<string>? current, string? input, out List<string> result, out string? error)
    {
        var existing = OutlookFolderPolicy.Split(OutlookFolderPolicy.Join(current)).ToList();
        result = existing;
        error = null;
        var typed = (input ?? "").Split(new[] { ',', (char)0x3001, ';', (char)10, (char)13 }, StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        if (typed.Count == 0) { error = "Type a folder name."; return false; }
        if (typed.Any(n => n.Contains('|'))) { error = "A folder name cannot contain \"|\"."; return false; }
        var merged = OutlookFolderPolicy.Split(OutlookFolderPolicy.Join(existing.Concat(typed))).ToList();
        if (merged.Count == existing.Count) { error = "Already watched."; return false; }
        result = merged;
        return true;
    }

    /// <summary>Parses the dock's distance from the top edge in DIPs (0-100).</summary>
    public static bool TryParseTopMargin(string? text, out double margin) =>
        double.TryParse(text?.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out margin)
        && double.IsFinite(margin) && margin >= TopMarginMin && margin <= TopMarginMax;

    /// <summary>The reminder lead time in minutes if it is one of the allowed choices, else the default (10).</summary>
    public static int NormalizeReminderLead(int minutes) => AgendaReminderPolicy.NormalizeLead(minutes);

    public static double ClampCpuGpuThreshold(double value) => ClampFinite(value, CpuGpuThresholdMin, CpuGpuThresholdMax, 90);
    public static double ClampTemperatureThreshold(double value) => ClampFinite(value, TemperatureThresholdMin, TemperatureThresholdMax, 85);
    public static double ClampTempColorThreshold(double value, double fallback) => ClampFinite(value, TempColorMin, TempColorMax, fallback);
    public static (double Warm, double Hot) NormalizeTempColorPair(double warm, double hot, double defaultWarm, double defaultHot)
    {
        double w = ClampTempColorThreshold(warm, defaultWarm);
        double h = ClampTempColorThreshold(hot, defaultHot);
        if (w > h) h = w;
        return (w, h);
    }
    /// <summary>One-time migration from the old single TemperatureAlertThreshold (default 85 °C) to separate CPU/GPU warm/hot limits.</summary>
    public static (double CpuWarm, double CpuHot, double GpuWarm, double GpuHot) MigrateLegacyTempThresholds(
        double legacyAlertThreshold, double cpuWarm, double cpuHot, double gpuWarm, double gpuHot)
    {
        if (double.IsFinite(legacyAlertThreshold)
            && Math.Abs(legacyAlertThreshold - 85) > 0.01
            && Math.Abs(cpuHot - DefaultCpuTempHot) < 0.01
            && Math.Abs(gpuHot - DefaultGpuTempHot) < 0.01)
        {
            double migratedHot = ClampTempColorThreshold(legacyAlertThreshold, DefaultCpuTempHot);
            double cw = Math.Min(ClampTempColorThreshold(cpuWarm, DefaultCpuTempWarm), migratedHot);
            double gw = Math.Min(ClampTempColorThreshold(gpuWarm, DefaultGpuTempWarm), migratedHot);
            return (cw, migratedHot, gw, migratedHot);
        }

        var cpu = NormalizeTempColorPair(cpuWarm, cpuHot, DefaultCpuTempWarm, DefaultCpuTempHot);
        var gpu = NormalizeTempColorPair(gpuWarm, gpuHot, DefaultGpuTempWarm, DefaultGpuTempHot);
        return (cpu.Warm, cpu.Hot, gpu.Warm, gpu.Hot);
    }
    public static double ClampGamingOffset(double value) => ClampFinite(value, GamingOffsetMin, GamingOffsetMax, 150);

    /// <summary>Accepts an IPv4/IPv6 address or a DNS host name for the ping target; returns it trimmed.</summary>
    public static bool TryNormalizePingHost(string? text, out string host)
    {
        host = (text ?? "").Trim();
        if (host.Length == 0 || host.Length > 253 || host.Any(char.IsWhiteSpace)) return false;
        if (System.Net.IPAddress.TryParse(host, out _)) return true;
        return Uri.CheckHostName(host) == UriHostNameType.Dns && !host.Contains('/') && !host.Contains(':');
    }

    private static double ClampFinite(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
