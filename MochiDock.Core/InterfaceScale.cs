using System;
using System.Collections.Generic;

namespace MochiDock.Core;

/// <summary>The scale to use now: <see cref="Percent"/> / 100, and whether it is lower than what the user asked for.</summary>
public readonly record struct InterfaceScaleResult(int Percent, double Scale, bool Limited);

/// <summary>
/// The app-only zoom of the dock and the Advanced Settings window, on top of Windows display scaling. The user picks a percentage from
/// <see cref="Presets"/>; the effective scale is lowered when the enlarged dock window would not fit the monitor's working area.
/// </summary>
public static class InterfaceScale
{
    public const int Min = 80, Max = 200, Default = 100;

    /// <summary>The sizes offered in the settings, in order.</summary>
    public static readonly IReadOnlyList<int> Presets = new[] { 80, 90, 100, 110, 125, 150, 175, 200 };

    /// <summary>The smallest scale a small screen may force (below the 80% the user can choose, but never unreadable).</summary>
    public const int FitFloor = 50;

    public static int Clamp(int percent) => Math.Clamp(percent, Min, Max);

    /// <summary>The next larger preset, or the largest one when already there.</summary>
    public static int Next(int percent)
    {
        percent = Clamp(percent);
        foreach (var preset in Presets) if (preset > percent) return preset;
        return Presets[^1];
    }

    /// <summary>The next smaller preset, or the smallest one when already there.</summary>
    public static int Previous(int percent)
    {
        percent = Clamp(percent);
        for (var i = Presets.Count - 1; i >= 0; i--) if (Presets[i] < percent) return Presets[i];
        return Presets[0];
    }

    /// <summary>
    /// The requested size, reduced so the window (<paramref name="baseWidth"/> x <paramref name="baseHeight"/> before scaling) still fits inside the
    /// working area (all in DIPs at the monitor's DPI). <paramref name="topMargin"/> is the gap kept above a floating dock. An unknown working area
    /// (zero or negative) does not limit anything.
    /// </summary>
    public static InterfaceScaleResult Effective(int percent, double workAreaWidth, double workAreaHeight, double baseWidth, double baseHeight, double topMargin = 0)
    {
        int requested = Clamp(percent);
        if (!(workAreaWidth > 0) || !(workAreaHeight > 0) || !(baseWidth > 0) || !(baseHeight > 0))
            return new InterfaceScaleResult(requested, requested / 100.0, false);

        double fit = Math.Min(workAreaWidth / baseWidth, Math.Max(0, workAreaHeight - Math.Max(0, topMargin)) / baseHeight);
        int fitPercent = (int)Math.Floor(fit * 100 + 1e-6);
        int effective = Math.Max(FitFloor, Math.Min(requested, fitPercent));
        return new InterfaceScaleResult(effective, effective / 100.0, effective < requested);
    }
}
