namespace MochiDock.Core;

/// <summary>Formatting and chart scaling for transfer rates (disk and network, in bytes per second; 1 KB = 1024 bytes).</summary>
public static class RateScale
{
    const double Kilo = 1024.0, Mega = 1024.0 * 1024.0;

    // Mantissas for a chart ceiling: a little finer than 1-2-5 so the trace fills more of the chart.
    static readonly double[] Mantissas = { 1.0, 1.5, 2.0, 3.0, 5.0, 7.5, 10.0 };

    /// <summary>"0 KB/s" for near-zero, then "12.5 KB/s" / "350 KB/s", and "2.4 MB/s" / "125 MB/s" from 1 MB/s up.</summary>
    public static string Format(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond <= 100) return "0 KB/s";
        if (bytesPerSecond < Mega)
        {
            double kb = bytesPerSecond / Kilo;
            return kb >= 100 ? $"{kb:0} KB/s" : $"{kb:0.#} KB/s";
        }
        double mb = bytesPerSecond / Mega;
        return mb >= 100 ? $"{mb:0} MB/s" : $"{mb:0.0} MB/s";
    }

    /// <summary>
    /// The top of a rate chart: the smallest round value at or above <paramref name="peakBytesPerSecond"/>, at least 1 KB/s.
    /// Below about 1 MB/s the value is round in KB (1, 1.5, 2, 3, 5, 7.5 x 10^n KB); above it, round in MB.
    /// </summary>
    public static double NiceCeiling(double peakBytesPerSecond)
    {
        double value = double.IsFinite(peakBytesPerSecond) && peakBytesPerSecond > Kilo ? peakBytesPerSecond : Kilo;
        double unit = value < 1000 * Kilo ? Kilo : Mega;
        double x = value / unit;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(x)));
        foreach (double m in Mantissas)
        {
            double candidate = m * magnitude;
            // A small tolerance keeps exact round values (for example 2.0) from rounding up a step through floating-point noise.
            if (x <= candidate * (1 + 1e-9)) return candidate * unit;
        }
        return 10 * magnitude * unit;
    }
}
