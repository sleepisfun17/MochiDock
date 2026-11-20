namespace MochiDock.Core;

/// <summary>
/// Lays out the Stats per-core bars in whole device pixels: every bar exactly as wide as the others, the same gap between each pair,
/// and the group centred. (A UniformGrid rounds each cell on its own, so 32 thin bars came out 2 and 3 pixels wide side by side.)
/// </summary>
public static class CoreBarLayout
{
    /// <summary>The bar width, the gap and the left edge (all in device pixels) for <paramref name="count"/> bars in <paramref name="availablePixels"/>.</summary>
    public static (int BarWidth, int Gap, int Left) Compute(double availablePixels, int count, double preferredGapPixels)
    {
        int width = (int)Math.Floor(Math.Max(0, availablePixels));
        if (count <= 0 || width <= 0) return (0, 0, 0);
        int gap = Math.Max(1, (int)Math.Round(preferredGapPixels));
        // Many cores in little room: shrink the gap before a bar would vanish.
        while (gap > 1 && (width - gap * (count - 1)) / count < 2) gap--;
        int bar = Math.Max(1, (width - gap * (count - 1)) / count);
        int used = bar * count + gap * (count - 1);
        int left = Math.Max(0, (width - used) / 2);
        return (bar, gap, left);
    }
}
