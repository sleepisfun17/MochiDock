using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>Whether a wattage was read from a sensor or worked out from other readings.</summary>
public enum PowerSource { Measured, Estimated }

/// <summary>One total-power sample for the Battery page. <see cref="Detail"/> explains the number (tooltip).</summary>
public sealed record PowerReading(double Watts, PowerSource Source, DateTimeOffset At, string Detail);

/// <summary>
/// Desktop estimate: a PC has no sensor for the board, fans, drives or PSU losses, so the total is the measured parts
/// (CPU package, GPU board, CPU-memory domain when exposed) plus a fixed allowance for the rest. It is always an estimate.
/// </summary>
public static class PowerEstimator
{
    /// <summary>Assumed draw of everything that cannot be measured (motherboard, RAM when not exposed, drives, fans, USB).</summary>
    public const double DefaultAllowanceWatts = 35;

    /// <summary>Null when neither the CPU nor the GPU has a measured reading (nothing to base an estimate on).</summary>
    public static PowerReading? Estimate(double? cpuPackageW, double? gpuW, double? memoryW, DateTimeOffset now, double allowanceW = DefaultAllowanceWatts, string? allowanceNote = null)
    {
        static double? Valid(double? w) => w is { } v && double.IsFinite(v) && v > 0 && v < 2000 ? v : null;
        var cpu = Valid(cpuPackageW);
        var gpu = Valid(gpuW);
        var mem = Valid(memoryW);
        if (cpu is null && gpu is null) return null;

        var parts = new List<string>();
        double total = Math.Max(0, allowanceW);
        if (cpu is { } c) { total += c; parts.Add($"CPU {c:0}"); }
        if (gpu is { } g) { total += g; parts.Add($"GPU {g:0}"); }
        if (mem is { } m) { total += m; parts.Add($"memory {m:0}"); }
        parts.Add($"{Math.Max(0, allowanceW):0} other");
        return new PowerReading(total, PowerSource.Estimated, now, string.Join(" + ", parts) + " W (" + (allowanceNote ?? "other is assumed") + ")");
    }
}

/// <summary>Converts the Windows battery rate (milliwatts) to a wattage and a label.</summary>
public static class BatteryRate
{
    /// <summary>BATTERY_UNKNOWN_RATE from the battery IOCTL.</summary>
    public const int UnknownRate = unchecked((int)0x80000000);

    /// <summary>
    /// Windows reports a negative rate while discharging and a positive one while charging. Returns the absolute draw in watts,
    /// or null when the rate is unknown, zero or implausible (above 400 W for a laptop battery).
    /// </summary>
    public static double? ToWatts(int rateMilliwatts)
    {
        if (rateMilliwatts == UnknownRate || rateMilliwatts == 0) return null;
        var watts = Math.Abs((long)rateMilliwatts) / 1000.0;
        return watts is > 0 and <= 400 ? watts : null;
    }

    public static PowerReading? Reading(int rateMilliwatts, DateTimeOffset now)
    {
        if (ToWatts(rateMilliwatts) is not { } w) return null;
        return new PowerReading(w, PowerSource.Measured, now, rateMilliwatts < 0 ? "Battery discharge rate" : "Battery charge rate");
    }
}

/// <summary>Fixed-size ring of recent samples for the sparkline, so memory never grows.</summary>
public sealed class PowerHistory
{
    readonly double[] _values;
    int _count, _next;

    public PowerHistory(int capacity = 60)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _values = new double[capacity];
    }

    public int Count => _count;

    public void Add(double watts)
    {
        if (!double.IsFinite(watts) || watts < 0) return;
        _values[_next] = watts;
        _next = (_next + 1) % _values.Length;
        if (_count < _values.Length) _count++;
    }

    public void Clear() { _count = 0; _next = 0; }

    /// <summary>Samples oldest first.</summary>
    public IReadOnlyList<double> Samples()
    {
        var result = new double[_count];
        var start = _count < _values.Length ? 0 : _next;
        for (var i = 0; i < _count; i++) result[i] = _values[(start + i) % _values.Length];
        return result;
    }

    public double? Average => _count == 0 ? null : Samples().Average();
    public double? Peak => _count == 0 ? null : Samples().Max();
}

/// <summary>Converts a Windows "Energy Meter" power counter (reported in milliwatts) to watts.</summary>
public static class EnergyMeterPower
{
    /// <summary>Null for zero, negative, non-finite or implausible (above 1000 W) values, so a counter that does not report cannot show 0 W.</summary>
    public static double? ToWatts(double milliwatts)
    {
        if (!double.IsFinite(milliwatts) || milliwatts <= 0) return null;
        double watts = milliwatts / 1000.0;
        return watts <= 1000 ? watts : null;
    }
}

/// <summary>The Stats header's Power chip on a desktop: the latest total and a small sparkline of the recent history.</summary>
public static class PowerChip
{
    /// <summary>"~142 W" for an estimate, "38.5 W" for a measured reading under 100 W, "-" when nothing could be read.</summary>
    public static string Text(PowerReading? reading) =>
        reading is null ? "-" : (reading.Source == PowerSource.Estimated ? "~" : "") + reading.Watts.ToString(reading.Watts < 100 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + " W";

    /// <summary>
    /// Points for the chip's 80 x 4 sparkline (the same box the CPU, GPU, Drives and Network chips draw in), scaled to the history's
    /// own range so changes stay visible; a flat line sits in the middle. Fewer than two samples give no line.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> SparkPoints(IReadOnlyList<double> samples)
    {
        if (samples.Count < 2) return Array.Empty<(double, double)>();
        double min = samples.Min(), range = samples.Max() - min;
        var points = new (double, double)[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            double fraction = range < 0.5 ? 0.5 : (samples[i] - min) / range;
            points[i] = (0.75 + i * 78.5 / (samples.Count - 1), 0.75 + (1 - fraction) * 2.5);
        }
        return points;
    }
}
