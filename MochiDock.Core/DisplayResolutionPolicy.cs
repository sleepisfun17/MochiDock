using System.Collections.Generic;

namespace MochiDock.Core;

public static class DisplayResolutionPolicy
{
    public static string GetAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0) return "—";
        double ratio = (double)width / height;
        if (Math.Abs(ratio - 16.0 / 9.0) < 0.04) return "16:9";
        if (Math.Abs(ratio - 16.0 / 10.0) < 0.04) return "16:10";
        if (Math.Abs(ratio - 4.0 / 3.0) < 0.04) return "4:3";
        if (Math.Abs(ratio - 5.0 / 4.0) < 0.04) return "5:4";
        if (Math.Abs(ratio - 21.0 / 9.0) < 0.04) return "21:9";
        if (Math.Abs(ratio - 32.0 / 9.0) < 0.04) return "32:9";
        return $"{width}:{height}";
    }

    public static string FormatLabel(int width, int height, bool isNative = false)
    {
        string ratio = GetAspectRatio(width, height);
        if (isNative) return $"{width}×{height} ({ratio} Native)";
        return $"{width}×{height} ({ratio})";
    }

    public static bool TryParse(string? input, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var parts = input.Split(['x', 'X', '×', ',', ' ', ':'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            int.TryParse(parts[0].Trim(), out int w) && w > 0 &&
            int.TryParse(parts[1].Trim(), out int h) && h > 0)
        {
            width = w;
            height = h;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The monitor's own (native) resolution: the largest of its supported modes. The screen's current size is no help once the
    /// resolution has been changed, because it is then the changed size.
    /// </summary>
    public static (int Width, int Height) PickNative(IEnumerable<(int Width, int Height)> supported, int fallbackWidth, int fallbackHeight)
    {
        var best = (Width: 0, Height: 0);
        foreach (var mode in supported)
        {
            if (mode.Width <= 0 || mode.Height <= 0) continue;
            long area = (long)mode.Width * mode.Height, bestArea = (long)best.Width * best.Height;
            if (area > bestArea || (area == bestArea && mode.Width > best.Width)) best = mode;
        }
        return best.Width > 0 ? best : (fallbackWidth > 0 ? fallbackWidth : 2560, fallbackHeight > 0 ? fallbackHeight : 1440);
    }

    public static (int Width, int Height) GetToggleResolution(
        int currentWidth,
        int currentHeight,
        int nativeWidth,
        int nativeHeight,
        string? preferredGamingRes)
    {
        if (!TryParse(preferredGamingRes, out int prefW, out int prefH))
        {
            prefW = 2304;
            prefH = 1440;
        }

        // If currently at the preferred gaming resolution, toggle back to native
        if (currentWidth == prefW && currentHeight == prefH)
        {
            return (nativeWidth > 0 ? nativeWidth : 2560, nativeHeight > 0 ? nativeHeight : 1440);
        }

        // Otherwise toggle to the preferred gaming resolution
        return (prefW, prefH);
    }
}

