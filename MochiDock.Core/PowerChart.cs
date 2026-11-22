using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MochiDock.Core;

/// <summary>
/// Shapes of the Battery page's Power chart, in a <c>width</c> x <c>height</c> box (y grows downwards).
/// The areas are stacked bottom to top: <see cref="OtherArea"/> (board, drives, fans: total minus the measured parts),
/// <see cref="CpuArea"/>, then <see cref="GpuArea"/>; their top edge is <see cref="TotalLine"/>. Without measured parts
/// (a laptop battery rate) <see cref="OtherArea"/> is the whole total and the other two are empty.
/// </summary>
public sealed record PowerChartGeometry(
    double Ceiling,
    string CeilingText,
    bool HasBreakdown,
    IReadOnlyList<ChartPoint> TotalLine,
    IReadOnlyList<ChartPoint> OtherArea,
    IReadOnlyList<ChartPoint> CpuArea,
    IReadOnlyList<ChartPoint> GpuArea,
    double Average,
    double Peak,
    double AverageY,
    ChartPoint PeakPoint);

public static class PowerChart
{
    // Round watt ceilings, so the scale label reads naturally and a steady draw does not fill the chart to the top.
    static readonly double[] Ceilings = [25, 50, 75, 100, 150, 200, 250, 300, 400, 500, 600, 800, 1000, 1200, 1500, 2000];

    /// <summary>The smallest round ceiling at least 10% above <paramref name="peak"/>; from 0 W, so the height shows real draw.</summary>
    public static double NiceCeiling(double peak)
    {
        double target = Math.Max(1, peak) * 1.1;
        foreach (double ceiling in Ceilings)
            if (target <= ceiling) return ceiling;
        return Math.Ceiling(target / 500) * 500;
    }

    /// <summary>
    /// Null with fewer than two samples (nothing to draw a line between). Samples are right-anchored in
    /// <paramref name="capacity"/> fixed slots (<see cref="HardwareChartMath.SlotX"/>), so the chart fills in from the right.
    /// </summary>
    public static PowerChartGeometry? Build(IReadOnlyList<PowerSample> samples, int capacity, double width, double height)
    {
        int count = samples.Count;
        if (count < 2 || width <= 0 || height <= 0) return null;

        bool breakdown = samples.Any(s => s.CpuW is not null || s.GpuW is not null);
        double peak = samples.Max(s => s.Total);
        double average = samples.Average(s => s.Total);
        double ceiling = NiceCeiling(peak);
        double Y(double watts) => height - Math.Clamp(watts / ceiling, 0, 1) * (height - 2);

        // Cumulative edges per sample: 0 | other | other + CPU | total (GPU on top, where game spikes show).
        var xs = new double[count];
        var other = new double[count];
        var cpuTop = new double[count];
        var total = new double[count];
        for (int i = 0; i < count; i++)
        {
            var s = samples[i];
            double cpu = Math.Clamp(s.CpuW ?? 0, 0, s.Total);
            double gpu = Math.Clamp(s.GpuW ?? 0, 0, s.Total - cpu);
            xs[i] = HardwareChartMath.SlotX(i, count, width, capacity);
            total[i] = s.Total;
            other[i] = s.Total - cpu - gpu;
            cpuTop[i] = other[i] + cpu;
        }

        IReadOnlyList<ChartPoint> Band(double[]? lower, double[] upper)
        {
            var points = new List<ChartPoint>(count * 2);
            for (int i = 0; i < count; i++) points.Add(new(xs[i], Y(upper[i])));
            for (int i = count - 1; i >= 0; i--) points.Add(new(xs[i], lower == null ? height : Y(lower[i])));
            return points;
        }

        var line = new List<ChartPoint>(count);
        for (int i = 0; i < count; i++) line.Add(new(xs[i], Y(total[i])));

        int peakIndex = 0;
        for (int i = 1; i < count; i++) if (total[i] >= total[peakIndex]) peakIndex = i;   // the most recent peak

        return new PowerChartGeometry(
            ceiling,
            ceiling.ToString("0", CultureInfo.InvariantCulture) + " W",
            breakdown,
            line,
            breakdown ? Band(null, other) : Band(null, total),
            breakdown ? Band(other, cpuTop) : Array.Empty<ChartPoint>(),
            breakdown ? Band(cpuTop, total) : Array.Empty<ChartPoint>(),
            average,
            peak,
            Y(average),
            line[peakIndex]);
    }
}
