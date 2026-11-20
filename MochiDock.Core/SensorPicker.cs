using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>One raw reading from a hardware sensor library, reduced to plain strings so selection stays testable.</summary>
public sealed record RawSensor(string HardwareType, string HardwareName, string SensorType, string SensorName, float? Value);

/// <summary>The sensor values chosen for the dock. A null value means no measured reading exists.</summary>
public sealed record SensorSnapshot(
    double? CpuTempC,
    double? GpuTempC,
    double? GpuClockMhz,
    double? GpuPowerW,
    string? GpuHardwareName,
    DateTimeOffset CapturedAt,
    double? CpuPackagePowerW = null,
    double? CpuMemoryPowerW = null)
{
    public static SensorSnapshot Empty { get; } = new(null, null, null, null, null, DateTimeOffset.MinValue);
}

/// <summary>Chooses the right CPU and GPU readings out of everything a sensor library reports.</summary>
public static class SensorPicker
{
    static readonly string[] CpuTempPreference = { "CPU Package", "Core (Tctl/Tdie)", "Core Max", "Tctl/Tdie", "Core (Tdie)", "CPU Total" };
    static readonly string[] GpuTempPreference = { "GPU Core", "GPU Edge", "GPU Hot Spot" };
    static readonly string[] GpuPowerPreference = { "GPU Package", "GPU Total Board Power", "GPU Board Power", "GPU Power", "GPU Core" };

    public static SensorSnapshot Pick(IReadOnlyList<RawSensor> sensors, string? gpuNameHint, DateTimeOffset now)
    {
        double? cpuTemp = PickCpuTemperature(sensors);
        var gpu = ChooseGpu(sensors, gpuNameHint);
        double? gpuTemp = null, gpuClock = null, gpuPower = null;
        if (gpu is not null)
        {
            gpuTemp = PickByName(gpu, "Temperature", GpuTempPreference, ValidTemp);
            gpuClock = PickByName(gpu, "Clock", new[] { "GPU Core" }, v => v > 0 && v < 10000);
            gpuPower = PickByName(gpu, "Power", GpuPowerPreference, v => v > 0 && v < 2000);
        }
        return new SensorSnapshot(cpuTemp, gpuTemp, gpuClock, gpuPower, gpu?.FirstOrDefault()?.HardwareName, now,
            PickCpuPower(sensors, "CPU Package"), PickCpuPower(sensors, "CPU Memory"));
    }

    /// <summary>A CPU power reading by exact sensor name ("CPU Package" on Intel and AMD, "CPU Memory" on Intel only); null when absent or implausible.</summary>
    public static double? PickCpuPower(IReadOnlyList<RawSensor> sensors, string sensorName)
    {
        var hit = sensors.FirstOrDefault(s => s.HardwareType == "Cpu" && s.SensorType == "Power"
            && string.Equals(s.SensorName, sensorName, StringComparison.OrdinalIgnoreCase)
            && s.Value is { } v && float.IsFinite(v) && v > 0 && v < 1000);
        return hit?.Value;
    }

    static bool ValidTemp(float v) => v > 1 && v < 130;

    public static double? PickCpuTemperature(IReadOnlyList<RawSensor> sensors)
    {
        var temps = sensors.Where(s => s.HardwareType == "Cpu" && s.SensorType == "Temperature" && s.Value is { } v && float.IsFinite(v) && ValidTemp(v)).ToList();
        foreach (var name in CpuTempPreference)
        {
            var hit = temps.FirstOrDefault(s => string.Equals(s.SensorName, name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit.Value;
        }
        // No package sensor: use the hottest core or die reading rather than guessing.
        // "Distance to TjMax" sensors are headroom values, not temperatures, so they are excluded.
        var cores = temps.Where(s => !s.SensorName.Contains("Distance", StringComparison.OrdinalIgnoreCase)
                                     && !s.SensorName.Contains("TjMax", StringComparison.OrdinalIgnoreCase)
                                     && (s.SensorName.Contains("Core", StringComparison.OrdinalIgnoreCase) || s.SensorName.StartsWith("CCD", StringComparison.OrdinalIgnoreCase))).ToList();
        return cores.Count > 0 ? cores.Max(s => s.Value) : null;
    }

    /// <summary>Picks one GPU: the adapter matching the detected name if possible, otherwise the first with a valid temperature.</summary>
    static List<RawSensor>? ChooseGpu(IReadOnlyList<RawSensor> sensors, string? hint)
    {
        var groups = sensors
            .Where(s => s.HardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.HardwareType + "|" + s.HardwareName)
            .Select(g => g.ToList())
            .ToList();
        if (groups.Count == 0) return null;

        bool HasTemp(List<RawSensor> g) => g.Any(s => s.SensorType == "Temperature" && s.Value is { } v && float.IsFinite(v) && ValidTemp(v));
        if (!string.IsNullOrWhiteSpace(hint))
        {
            var named = groups.FirstOrDefault(g => NamesMatch(g[0].HardwareName, hint!) && HasTemp(g));
            if (named is not null) return named;
        }
        return groups.FirstOrDefault(HasTemp) ?? groups[0];
    }

    static bool NamesMatch(string a, string b)
    {
        static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        string x = Norm(a), y = Norm(b);
        return x.Length > 0 && y.Length > 0 && (x.Contains(y) || y.Contains(x));
    }

    static double? PickByName(List<RawSensor> gpu, string sensorType, string[] preference, Func<float, bool> valid)
    {
        var list = gpu.Where(s => s.SensorType == sensorType && s.Value is { } v && float.IsFinite(v) && valid(v)).ToList();
        foreach (var name in preference)
        {
            var hit = list.FirstOrDefault(s => string.Equals(s.SensorName, name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit.Value;
        }
        return null;
    }
}
