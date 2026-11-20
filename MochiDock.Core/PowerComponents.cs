using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MochiDock.Core;

public enum PowerComponentKind { Motherboard, Memory, Nvme, SataSsd, Hdd35, Hdd25, Fan, Pump, Rgb, Usb, Other }

/// <summary>
/// One part of the PC without a power sensor (board, RAM, drive, fan...). <see cref="Watts"/> is the estimated average draw of ONE unit;
/// it is a typical figure, not a measurement. <see cref="Detected"/> items were found by the scanner, the others were added by the user.
/// </summary>
public sealed class PowerComponent
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public PowerComponentKind Kind { get; set; } = PowerComponentKind.Other;
    public int Count { get; set; } = 1;
    public double Watts { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Detected { get; set; }
    /// <summary>True once the user changed the watts, count or switch, so a rescan keeps those values.</summary>
    public bool Edited { get; set; }
}

/// <summary>Typical average draw per unit, used for detected items and for "Reset to typical values".</summary>
public static class PowerComponentDefaults
{
    /// <summary>The old single allowance for everything unmeasured, used while the component list is empty.</summary>
    public const double FallbackAllowanceWatts = PowerEstimator.DefaultAllowanceWatts;

    public static double Watts(PowerComponentKind kind, bool ddr5 = false) => kind switch
    {
        PowerComponentKind.Motherboard => 20,
        PowerComponentKind.Memory => ddr5 ? 4 : 3,
        PowerComponentKind.Nvme => 5,
        PowerComponentKind.SataSsd => 2.5,
        PowerComponentKind.Hdd35 => 7,
        PowerComponentKind.Hdd25 => 2,
        PowerComponentKind.Fan => 2,
        PowerComponentKind.Pump => 5,
        PowerComponentKind.Rgb => 3,
        PowerComponentKind.Usb => 1,
        _ => 5,
    };

    public static string Label(PowerComponentKind kind) => kind switch
    {
        PowerComponentKind.Motherboard => "Motherboard + chipset",
        PowerComponentKind.Memory => "Memory stick",
        PowerComponentKind.Nvme => "NVMe / M.2 SSD",
        PowerComponentKind.SataSsd => "SATA SSD",
        PowerComponentKind.Hdd35 => "3.5\" hard drive",
        PowerComponentKind.Hdd25 => "2.5\" hard drive",
        PowerComponentKind.Fan => "Case fan",
        PowerComponentKind.Pump => "Pump / AIO",
        PowerComponentKind.Rgb => "RGB / controller",
        PowerComponentKind.Usb => "USB device",
        _ => "Other",
    };
}

public static class PowerAllowance
{
    public const double MaxItemWatts = 300;

    /// <summary>Watts of one item (per-unit watts times count, each clamped), 0 when switched off.</summary>
    public static double ItemWatts(PowerComponent c)
    {
        if (!c.Enabled) return 0;
        var watts = double.IsFinite(c.Watts) ? Math.Clamp(c.Watts, 0, MaxItemWatts) : 0;
        return Math.Min(MaxItemWatts, watts * Math.Clamp(c.Count, 0, 99));
    }

    /// <summary>Sum of everything switched on. An empty list is "not set up yet": the old fixed allowance is used instead.</summary>
    public static double Total(IReadOnlyCollection<PowerComponent>? components) =>
        components is null || components.Count == 0 ? PowerComponentDefaults.FallbackAllowanceWatts : components.Sum(ItemWatts);

    /// <summary>"board 20 + memory 2x4 + NVMe 5 = 33 W" for the tooltip (grouped by kind, switched-off items left out).</summary>
    public static string Describe(IReadOnlyCollection<PowerComponent>? components)
    {
        if (components is null || components.Count == 0) return $"{PowerComponentDefaults.FallbackAllowanceWatts:0} W assumed for everything else";
        var parts = components.Where(c => c.Enabled && ItemWatts(c) > 0).GroupBy(c => c.Kind)
            .Select(g => $"{Short(g.Key)} {g.Sum(ItemWatts):0.#}").ToList();
        return parts.Count == 0 ? "no other components" : string.Join(" + ", parts) + $" = {Total(components):0.#} W";
    }

    static string Short(PowerComponentKind kind) => kind switch
    {
        PowerComponentKind.Motherboard => "board",
        PowerComponentKind.Memory => "memory",
        PowerComponentKind.Nvme => "NVMe",
        PowerComponentKind.SataSsd => "SSD",
        PowerComponentKind.Hdd35 or PowerComponentKind.Hdd25 => "HDD",
        PowerComponentKind.Fan => "fans",
        PowerComponentKind.Pump => "pump",
        PowerComponentKind.Rgb => "RGB",
        PowerComponentKind.Usb => "USB",
        _ => "other",
    };
}

public static class PowerComponentMerge
{
    /// <summary>
    /// Folds a fresh scan into the saved list: a known item keeps the user's edits, a new one is added, a detected item that is gone is dropped
    /// (an edited one too: the part no longer exists), and items the user added are never touched.
    /// </summary>
    public static List<PowerComponent> Merge(IEnumerable<PowerComponent> existing, IEnumerable<PowerComponent> detected)
    {
        var saved = existing.ToList();
        var fresh = detected.ToList();
        var result = new List<PowerComponent>();
        foreach (var scanned in fresh)
        {
            var known = saved.FirstOrDefault(s => s.Detected && string.Equals(s.Id, scanned.Id, StringComparison.OrdinalIgnoreCase));
            if (known is null) { result.Add(scanned); continue; }
            known.Name = scanned.Name;
            known.Kind = scanned.Kind;
            if (!known.Edited) { known.Watts = scanned.Watts; known.Count = scanned.Count; }
            result.Add(known);
        }
        result.AddRange(saved.Where(s => !s.Detected));
        return result;
    }

    public static string NewId(string name) => "user:" + Guid.NewGuid().ToString("N")[..8] + ":" + name.Trim().ToLower(CultureInfo.InvariantCulture);
}
