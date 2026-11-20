using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MochiDock.Core;

/// <summary>One logged run of the app: how long it was sampled, the energy used and the average and peak draw.</summary>
public sealed class PowerSessionRecord
{
    public Guid Id { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    /// <summary>Seconds that were actually sampled (sleep and other gaps are not counted).</summary>
    public double SampledSeconds { get; set; }
    public double EnergyWh { get; set; }
    public double AverageW { get; set; }
    public double PeakW { get; set; }
    /// <summary>"Measured", "Estimated" or "Mixed".</summary>
    public string Source { get; set; } = "Estimated";
    /// <summary>True while the session is still running; a record found open at startup ended without a clean exit.</summary>
    public bool Open { get; set; }
    /// <summary>True when the app did not exit cleanly (crash, power loss): the end time is the last checkpoint.</summary>
    public bool EndedUnexpectedly { get; set; }
}

/// <summary>
/// Adds power samples up into a session: energy by the trapezoid rule over the time between samples.
/// A gap longer than <see cref="MaxGapSeconds"/> (sleep, hibernate) is not integrated, so it never adds energy.
/// </summary>
public sealed class PowerSessionTracker
{
    public const double MaxGapSeconds = 60;

    readonly Guid _id;
    readonly DateTimeOffset _start;
    DateTimeOffset? _lastAt;
    double _lastW, _wattHours, _sampled, _peak, _measuredSeconds, _estimatedSeconds;

    public PowerSessionTracker(DateTimeOffset startUtc, Guid? id = null)
    {
        _start = startUtc;
        _id = id ?? Guid.NewGuid();
    }

    public bool HasData => _sampled > 0;

    public void Add(DateTimeOffset at, double watts, PowerSource source)
    {
        if (!double.IsFinite(watts) || watts < 0) return;
        if (_lastAt is { } last)
        {
            var dt = (at - last).TotalSeconds;
            if (dt <= 0) return;
            if (dt <= MaxGapSeconds)
            {
                _wattHours += (_lastW + watts) / 2 * dt / 3600;
                _sampled += dt;
                if (source == PowerSource.Measured) _measuredSeconds += dt; else _estimatedSeconds += dt;
            }
        }
        _peak = Math.Max(_peak, watts);
        _lastAt = at;
        _lastW = watts;
    }

    public PowerSessionRecord Snapshot(DateTimeOffset nowUtc, bool open)
    {
        var source = _measuredSeconds > 0 && _estimatedSeconds > 0 ? "Mixed" : _measuredSeconds > 0 ? "Measured" : "Estimated";
        return new PowerSessionRecord
        {
            Id = _id,
            StartUtc = _start,
            EndUtc = nowUtc,
            SampledSeconds = _sampled,
            EnergyWh = _wattHours,
            AverageW = _sampled > 0 ? _wattHours * 3600 / _sampled : 0,
            PeakW = _peak,
            Source = source,
            Open = open,
        };
    }
}

/// <summary>Chooses between the measured laptop battery rate and the CPU+GPU estimate.</summary>
public static class PowerSampler
{
    /// <summary>
    /// A laptop on battery reports its whole-machine draw as a discharge rate, which is a measurement. On AC the rate is the charge rate
    /// (not the machine's draw) and on a desktop there is no rate, so those fall back to the estimate from CPU, GPU and memory sensors.
    /// </summary>
    public static PowerReading? Choose(bool laptop, bool onAc, int? batteryRateMilliwatts, double? cpuW, double? gpuW, double? memoryW, DateTimeOffset now, double allowanceW = PowerEstimator.DefaultAllowanceWatts, string? allowanceNote = null)
    {
        if (laptop && !onAc && batteryRateMilliwatts is < 0 and not BatteryRate.UnknownRate && BatteryRate.Reading(batteryRateMilliwatts.Value, now) is { } measured)
            return measured;
        return PowerEstimator.Estimate(cpuW, gpuW, memoryW, now, allowanceW, allowanceNote);
    }
}

/// <summary>Capped, newest-first history and the texts shown for a session.</summary>
public static class PowerSessionFormat
{
    public const int HistoryCap = 30;

    /// <summary>The record first (replacing one with the same id), the rest newest first, capped at <see cref="HistoryCap"/>.</summary>
    public static List<PowerSessionRecord> AddToHistory(IEnumerable<PowerSessionRecord> existing, PowerSessionRecord added) =>
        new[] { added }.Concat(existing.Where(r => r.Id != added.Id)).Take(HistoryCap).ToList();

    /// <summary>"0.42 kWh" from 100 Wh up, otherwise "85 Wh".</summary>
    public static string Energy(double wattHours) =>
        wattHours >= 100 ? (wattHours / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " kWh" : wattHours.ToString("0", CultureInfo.InvariantCulture) + " Wh";

    public static string Watts(double watts) => watts.ToString("0", CultureInfo.InvariantCulture) + " W";

    /// <summary>"Today 18:02-23:40 · 0.61 kWh · avg 108 W" (the "~" marks an estimate); without the average for narrow captions.</summary>
    public static string Summary(PowerSessionRecord r, DateTime nowLocal, bool includeAverage = true)
    {
        var start = r.StartUtc.ToLocalTime().DateTime;
        var end = r.EndUtc.ToLocalTime().DateTime;
        var day = start.Date == nowLocal.Date ? "Today" : start.Date == nowLocal.Date.AddDays(-1) ? "Yesterday" : DateDisplay.Short(start);
        var prefix = r.Source == "Measured" ? "" : "~";
        var text = $"{day} {start:HH:mm}-{end:HH:mm} · {prefix}{Energy(r.EnergyWh)}";
        return includeAverage ? text + $" · avg {prefix}{Watts(r.AverageW)}" : text;
    }
}
