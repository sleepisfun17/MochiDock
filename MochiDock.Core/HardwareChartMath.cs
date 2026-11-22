namespace MochiDock.Core;

/// <summary>Pure coordinate pair used before converting to WPF <c>PointCollection</c> at the UI boundary.</summary>
public readonly record struct ChartPoint(double X, double Y);

/// <summary>Pure rolling-buffer and chart coordinate normalization math for hardware graphs and chip sparklines.</summary>
public static class HardwareChartMath
{
    public const int DefaultHistoryCapacity = 40;
    private static readonly float[] FpsCeilings = [30f, 60f, 75f, 120f, 144f, 165f, 240f, 360f];

    public static void PushBounded(Queue<double> queue, double value, int maxSamples = DefaultHistoryCapacity)
    {
        queue.Enqueue(value);
        while (queue.Count > maxSamples) queue.Dequeue();
    }

    /// <summary>
    /// X of sample <paramref name="index"/> of <paramref name="count"/> in a chart that holds <paramref name="capacity"/> samples:
    /// slots are a fixed width apart and the newest sample sits at the right edge, so a buffer that is still filling grows in
    /// from the right instead of stretching its first two samples across the whole chart. A full buffer spans 0..width as before.
    /// </summary>
    public static double SlotX(int index, int count, double width, int capacity = DefaultHistoryCapacity)
    {
        double step = width / Math.Max(1, Math.Max(capacity, count) - 1);
        return width - (count - 1 - index) * step;
    }

    public static float PickFpsCeiling(double peakFps)
    {
        foreach (float candidate in FpsCeilings)
            if (peakFps <= candidate) return candidate;
        return (float)(Math.Ceiling(peakFps / 120.0) * 120.0);
    }

    public static (IReadOnlyList<ChartPoint> Line, IReadOnlyList<ChartPoint> Fill) BuildAreaSeries(
        IReadOnlyCollection<double> samples, double width, double height, double maxVal, int capacity = DefaultHistoryCapacity)
    {
        if (samples.Count == 0)
            return (Array.Empty<ChartPoint>(), Array.Empty<ChartPoint>());

        int count = samples.Count;
        var line = new List<ChartPoint>(count);
        // The area starts under the oldest sample, not at the left edge, while the buffer is still filling.
        var fill = new List<ChartPoint>(count + 2) { new(SlotX(0, count, width, capacity), height) };
        double clampedMax = Math.Max(1.0, maxVal);
        int idx = 0;

        foreach (double val in samples)
        {
            double x = SlotX(idx, count, width, capacity);
            double frac = Math.Clamp(val / clampedMax, 0.0, 1.0);
            double y = height - (frac * (height - 3.0));
            var pt = new ChartPoint(x, y);
            line.Add(pt);
            fill.Add(pt);
            idx++;
        }

        fill.Add(new(width, height));
        return (line, fill);
    }

    public static (IReadOnlyList<ChartPoint> UpLine, IReadOnlyList<ChartPoint> UpFill, IReadOnlyList<ChartPoint> DownLine, IReadOnlyList<ChartPoint> DownFill, double NiceCeiling) BuildMirroredSeries(
        IReadOnlyCollection<double> upSamples, IReadOnlyCollection<double> downSamples, double width, double height, int capacity = DefaultHistoryCapacity)
    {
        double maxUp = upSamples.Count > 0 ? upSamples.Max() : 0.0;
        double maxDown = downSamples.Count > 0 ? downSamples.Max() : 0.0;
        double niceCeil = RateScale.NiceCeiling(Math.Max(maxUp, maxDown));

        int count = Math.Max(upSamples.Count, downSamples.Count);
        if (count == 0)
            return (Array.Empty<ChartPoint>(), Array.Empty<ChartPoint>(), Array.Empty<ChartPoint>(), Array.Empty<ChartPoint>(), niceCeil);

        double mid = Math.Round(height / 2.0);
        double halfH = mid - 4.0;

        // Each half is right-anchored on its own count, so both end at the newest sample.
        int upCount = upSamples.Count;
        var upLine = new List<ChartPoint>(upCount);
        var upFill = new List<ChartPoint>(upCount + 2) { new(upCount > 0 ? SlotX(0, upCount, width, capacity) : width, mid) };
        int i = 0;
        foreach (double val in upSamples)
        {
            double x = SlotX(i++, upCount, width, capacity);
            double frac = Math.Clamp(val / niceCeil, 0.0, 1.0);
            var pt = new ChartPoint(x, mid - (frac * halfH));
            upLine.Add(pt);
            upFill.Add(pt);
        }
        upFill.Add(new(width, mid));

        int downCount = downSamples.Count;
        var downLine = new List<ChartPoint>(downCount);
        var downFill = new List<ChartPoint>(downCount + 2) { new(downCount > 0 ? SlotX(0, downCount, width, capacity) : width, mid) };
        i = 0;
        foreach (double val in downSamples)
        {
            double x = SlotX(i++, downCount, width, capacity);
            double frac = Math.Clamp(val / niceCeil, 0.0, 1.0);
            var pt = new ChartPoint(x, mid + (frac * halfH));
            downLine.Add(pt);
            downFill.Add(pt);
        }
        downFill.Add(new(width, mid));

        return (upLine, upFill, downLine, downFill, niceCeil);
    }

    public static IReadOnlyList<ChartPoint> BuildChipSparkline(IReadOnlyCollection<double> samples, double ceiling, int capacity = DefaultHistoryCapacity)
    {
        if (samples.Count == 0) return Array.Empty<ChartPoint>();
        int count = samples.Count;
        var result = new List<ChartPoint>(count);
        double denom = Math.Max(1.0, ceiling);
        int index = 0;
        foreach (double value in samples)
        {
            double x = 0.75 + SlotX(index++, count, 78.5, capacity);
            double fraction = Math.Clamp(value / denom, 0.0, 1.0);
            result.Add(new(x, 0.75 + (1.0 - fraction) * 2.5));
        }
        return result;
    }

    public static (IReadOnlyList<ChartPoint> Points, double GuideY, double Ceiling) BuildFrameTimeSeries(
        IReadOnlyList<double> worst, double width, double height)
    {
        double ceiling = Math.Max(33.4, worst.Max());
        var sorted = worst.OrderBy(v => v).ToArray();
        double guide = Math.Min(2 * sorted[sorted.Length / 2], ceiling);
        var points = new List<ChartPoint>(worst.Count);
        int denom = Math.Max(1, worst.Count - 1);
        for (int i = 0; i < worst.Count; i++)
            points.Add(new(i * width / denom, height - Math.Clamp(worst[i] / ceiling, 0.0, 1.0) * (height - 2.0)));
        double guideY = height - Math.Clamp(guide / ceiling, 0.0, 1.0) * (height - 2.0);
        return (points, guideY, ceiling);
    }
}
