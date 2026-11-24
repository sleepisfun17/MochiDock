using System.Globalization;

namespace MochiDock.Core;

public enum TaskbarArrowStyle { Arrow, Triangle, Emoji }

public enum TaskbarPlacementMode { LeftOfTray, AfterTaskButtons }

/// <summary>Visual styling options for the taskbar readout (empty strings / 0 mean automatic defaults).</summary>
public sealed record TaskbarAppearance
{
    public string FontFamily { get; init; } = "";
    public double FontSize { get; init; } = 0;
    public bool Bold { get; init; } = false;
    public string LabelColorHex { get; init; } = "";
    public string ValueColorHex { get; init; } = "";

    public static TaskbarAppearance Default { get; } = new();
}

/// <summary>Pure font sizing, placement parsing, and WCAG-safe colour resolution for the taskbar readout.</summary>
public static class TaskbarStyle
{
    public const int MinOffsetPx = -500;
    public const int MaxOffsetPx = 500;
    public const double MinFontSize = 8;
    public const double MaxFontSize = 28;
    public const double MaxAutoFontSize = 24;
    public const double MinContrastRatio = 4.5;

    public static int ClampOffset(int px) => Math.Clamp(px, MinOffsetPx, MaxOffsetPx);

    public static double ClampFontSize(double size) =>
        !double.IsFinite(size) || size <= 0 ? 0 : Math.Clamp(Math.Round(size, 1), MinFontSize, MaxFontSize);

    public static TaskbarPlacementMode ParsePlacement(string? value) =>
        string.Equals(value?.Trim(), "AfterTaskButtons", StringComparison.OrdinalIgnoreCase)
            ? TaskbarPlacementMode.AfterTaskButtons
            : TaskbarPlacementMode.LeftOfTray;

    public static string FormatPlacement(TaskbarPlacementMode mode) =>
        mode == TaskbarPlacementMode.AfterTaskButtons ? "AfterTaskButtons" : "LeftOfTray";

    public static TaskbarArrowStyle ParseArrowStyle(string? value)
    {
        if (string.Equals(value?.Trim(), "Triangle", StringComparison.OrdinalIgnoreCase)) return TaskbarArrowStyle.Triangle;
        if (string.Equals(value?.Trim(), "Emoji", StringComparison.OrdinalIgnoreCase)) return TaskbarArrowStyle.Emoji;
        return TaskbarArrowStyle.Arrow;
    }

    public static string FormatArrowStyle(TaskbarArrowStyle style) => style switch
    {
        TaskbarArrowStyle.Triangle => "Triangle",
        TaskbarArrowStyle.Emoji => "Emoji",
        _ => "Arrow",
    };

    /// <summary>
    /// Computes the rendered DIP font size from the live taskbar height.
    /// Auto (<paramref name="requestedSize"/> &lt;= 0) scales proportionally to fill the taskbar height (including tall Windhawk taskbars).
    /// Explicit sizes clamp to avoid clipping top or bottom.
    /// </summary>
    public static double ComputeFontSize(double heightDip, double lineHeightRatio, bool oneLine, double requestedSize)
    {
        double ratio = double.IsFinite(lineHeightRatio) && lineHeightRatio > 0.1 ? lineHeightRatio : 1.33;
        int lines = oneLine ? 1 : 2;
        double maxSafe = Math.Max(MinFontSize, (heightDip - 4) / (lines * ratio));
        if (!double.IsFinite(requestedSize) || requestedSize <= 0)
        {
            double verticalPad = Math.Clamp(heightDip * 0.22, 6, 16);
            double avail = Math.Max(8, heightDip - verticalPad);
            double cap = oneLine ? 12 : MaxAutoFontSize;
            return Math.Min(maxSafe, Math.Clamp(avail / (lines * ratio), MinFontSize, cap));
        }
        return Math.Min(Math.Clamp(requestedSize, MinFontSize, MaxFontSize), maxSafe);
    }

    public static bool TryParseHexColor(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6) return false;
        return byte.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }

    public static string NormalizeHexColor(string? text) =>
        TryParseHexColor(text, out byte r, out byte g, out byte b) ? $"#{r:X2}{g:X2}{b:X2}" : "";

    /// <summary>
    /// Resolves a custom hex colour for the active taskbar theme. Returns the default taskbar ink if empty/invalid,
    /// returns the exact colour if it already achieves 4.5:1 contrast, or preserves hue while adjusting lightness to reach 4.5:1.
    /// </summary>
    public static (byte R, byte G, byte B) ResolveColor(string? customHex, bool darkTaskbar)
    {
        (byte R, byte G, byte B) ground = darkTaskbar ? ((byte)0x20, (byte)0x20, (byte)0x20) : ((byte)0xF3, (byte)0xF3, (byte)0xF3);
        (byte R, byte G, byte B) fallback = darkTaskbar ? ((byte)0xFF, (byte)0xFF, (byte)0xFF) : ((byte)0x1B, (byte)0x1B, (byte)0x1B);
        if (!TryParseHexColor(customHex, out byte r, out byte g, out byte b)) return fallback;

        var raw = (r, g, b);
        if (Contrast(raw, ground) >= MinContrastRatio) return raw;

        RgbToHsl(r, g, b, out double h, out double s, out double l0);
        double lo = darkTaskbar ? l0 : 0.0;
        double hi = darkTaskbar ? 1.0 : l0;
        var best = fallback;
        for (int i = 0; i < 24; i++)
        {
            double mid = (lo + hi) * 0.5;
            var cand = HslToRgb(h, s, mid);
            if (Contrast(cand, ground) >= MinContrastRatio)
            {
                best = cand;
                if (darkTaskbar) hi = mid; else lo = mid;
            }
            else
            {
                if (darkTaskbar) lo = mid; else hi = mid;
            }
        }

        if (Contrast(best, ground) >= MinContrastRatio) return best;

        (byte tr, byte tg, byte tb) = darkTaskbar ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0);
        double blo = 0, bhi = 1;
        for (int i = 0; i < 24; i++)
        {
            double mid = (blo + bhi) * 0.5;
            var cand = (
                (byte)Math.Round(r + (tr - r) * mid),
                (byte)Math.Round(g + (tg - g) * mid),
                (byte)Math.Round(b + (tb - b) * mid));
            if (Contrast(cand, ground) >= MinContrastRatio)
            {
                best = cand;
                bhi = mid;
            }
            else blo = mid;
        }
        return best;
    }

    public static double Contrast((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        double hi = Math.Max(la, lb);
        double lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuminance((byte R, byte G, byte B) c)
    {
        static double Linear(byte v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    private static void RgbToHsl(byte rByte, byte gByte, byte bByte, out double h, out double s, out double l)
    {
        double r = rByte / 255.0, g = gByte / 255.0, b = bByte / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        l = (max + min) * 0.5;
        if (Math.Abs(max - min) < 1e-9) { h = 0; s = 0; return; }
        double d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h /= 6.0;
    }

    private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
    {
        if (s <= 1e-9)
        {
            byte v = (byte)Math.Clamp(Math.Round(l * 255.0), 0, 255);
            return (v, v, v);
        }
        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return (
            (byte)Math.Clamp(Math.Round(HueToRgb(p, q, h + 1.0 / 3) * 255.0), 0, 255),
            (byte)Math.Clamp(Math.Round(HueToRgb(p, q, h) * 255.0), 0, 255),
            (byte)Math.Clamp(Math.Round(HueToRgb(p, q, h - 1.0 / 3) * 255.0), 0, 255));
    }
}

/// <summary>Pure taskbar client X placement for LeftOfTray and AfterTaskButtons in both LTR and WS_EX_LAYOUTRTL.</summary>
public static class TaskbarLayout
{
    public static int ComputeClientX(
        TaskbarPlacementMode mode,
        int offsetPx,
        bool rtl,
        int taskbarLeft,
        int taskbarRight,
        int trayLeft,
        int trayRight,
        int widgetWidth,
        int gapPx,
        (int Left, int Right)? widgetsButton,
        (int Left, int Right)? taskButtons)
    {
        offsetPx = TaskbarStyle.ClampOffset(offsetPx);

        int LeftOfTrayBase()
        {
            if (rtl)
            {
                int s = trayRight;
                if (widgetsButton is { } w && w.Left < s + widgetWidth && w.Right > s) s = w.Right;
                return s;
            }
            else
            {
                int s = trayLeft - widgetWidth;
                if (widgetsButton is { } w && w.Right > s && w.Left < trayLeft) s = w.Left - widgetWidth;
                return s;
            }
        }

        int screenLeft;
        if (mode == TaskbarPlacementMode.AfterTaskButtons && taskButtons is { } tb)
        {
            if (rtl)
            {
                int minLeft = trayRight;
                if (widgetsButton is { } w && w.Right > trayRight && w.Left < tb.Left)
                    minLeft = Math.Max(minLeft, w.Right);
                int baseLeft = Math.Max(tb.Left - gapPx - widgetWidth, minLeft);
                screenLeft = baseLeft + offsetPx;
            }
            else
            {
                int maxRight = trayLeft;
                if (widgetsButton is { } w && w.Left < trayLeft && w.Right > tb.Right)
                    maxRight = Math.Min(maxRight, w.Left);
                int baseLeft = Math.Min(tb.Right + gapPx, maxRight - widgetWidth);
                screenLeft = baseLeft + offsetPx;
            }
        }
        else
        {
            screenLeft = LeftOfTrayBase() + offsetPx;
        }

        screenLeft = Math.Clamp(screenLeft, taskbarLeft, Math.Max(taskbarLeft, taskbarRight - widgetWidth));
        return rtl ? taskbarRight - screenLeft - widgetWidth : screenLeft - taskbarLeft;
    }
}
