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

    public static float PickFpsCeiling(double peakFps)
    {
        foreach (float candidate in FpsCeilings)
            if (peakFps <= candidate) return candidate;
        return (float)(Math.Ceiling(peakFps / 120.0) * 120.0);
    }

    public static (IReadOnlyList<ChartPoint> Line, IReadOnlyList<ChartPoint> Fill) BuildAreaSeries(
        IReadOnlyCollection<double> samples, double width, double height, double maxVal)
    {
        if (samples.Count == 0)
            return (Array.Empty<ChartPoint>(), Array.Empty<ChartPoint>());

        var line = new List<ChartPoint>(samples.Count);
        var fill = new List<ChartPoint>(samples.Count + 2) { new(0, height) };
        double wStep = width / Math.Max(1, samples.Count - 1);
        double clampedMax = Math.Max(1.0, maxVal);
        int idx = 0;

        foreach (double val in samples)
        {
            double x = idx * wStep;
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
        IReadOnlyCollection<double> upSamples, IReadOnlyCollection<double> downSamples, double width, double height)
    {
        double maxUp = upSamples.Count > 0 ? upSamples.Max() : 0.0;
        double maxDown = downSamples.Count > 0 ? downSamples.Max() : 0.0;
        double niceCeil = RateScale.NiceCeiling(Math.Max(maxUp, maxDown));

        int count = Math.Max(upSamples.Count, downSamples.Count);
        if (count == 0)
            return (Array.Empty<ChartPoint>(), Array.Empty<ChartPoint>(), Array.Empty<ChartPoint>(), Array.Empty<ChartPoint>(), niceCeil);

        double mid = Math.Round(height / 2.0);
        double halfH = mid - 4.0;
        double wStep = width / Math.Max(1, count - 1);

        var upLine = new List<ChartPoint>(upSamples.Count);
        var upFill = new List<ChartPoint>(upSamples.Count + 2) { new(0, mid) };
        int i = 0;
        foreach (double val in upSamples)
        {
            double x = i++ * wStep;
            double frac = Math.Clamp(val / niceCeil, 0.0, 1.0);
            var pt = new ChartPoint(x, mid - (frac * halfH));
            upLine.Add(pt);
            upFill.Add(pt);
        }
        upFill.Add(new(width, mid));

        var downLine = new List<ChartPoint>(downSamples.Count);
        var downFill = new List<ChartPoint>(downSamples.Count + 2) { new(0, mid) };
        i = 0;
        foreach (double val in downSamples)
        {
            double x = i++ * wStep;
            double frac = Math.Clamp(val / niceCeil, 0.0, 1.0);
            var pt = new ChartPoint(x, mid + (frac * halfH));
            downLine.Add(pt);
            downFill.Add(pt);
        }
        downFill.Add(new(width, mid));

        return (upLine, upFill, downLine, downFill, niceCeil);
    }

    public static IReadOnlyList<ChartPoint> BuildChipSparkline(IReadOnlyCollection<double> samples, double ceiling)
    {
        if (samples.Count == 0) return Array.Empty<ChartPoint>();
        var result = new List<ChartPoint>(samples.Count);
        double wStep = 78.5 / Math.Max(1, samples.Count - 1);
        double denom = Math.Max(1.0, ceiling);
        int index = 0;
        foreach (double value in samples)
        {
            double x = 0.75 + index++ * wStep;
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
