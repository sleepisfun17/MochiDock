using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MochiDock.Controls;

// Center a stable numeral band rather than the font's asymmetric ascent/descent box.
public sealed class CompactTextLine : Decorator
{
    private sealed class Metrics { public string Key = ""; public double Center; }
    private static readonly ConditionalWeakTable<TextBlock, Metrics> Cache = new();

    public static double NumeralCenter(TextBlock text)
    {
        double dpi = VisualTreeHelper.GetDpi(text).PixelsPerDip;
        string key = $"{text.FontFamily.Source}|{text.FontStyle}|{text.FontWeight}|{text.FontStretch}|{text.FontSize:R}|{dpi:R}|{text.FlowDirection}";
        var metrics = Cache.GetValue(text, static _ => new Metrics());
        if (metrics.Key == key) return metrics.Center;
        var reference = new FormattedText("0123456789", CultureInfo.InvariantCulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize, System.Windows.Media.Brushes.Black, dpi);
        var bounds = reference.BuildGeometry(new System.Windows.Point()).Bounds;
        metrics.Key = key;
        metrics.Center = bounds.IsEmpty ? reference.Height / 2 : bounds.Top + bounds.Height / 2;
        return metrics.Center;
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size size)
    {
        if (Child is TextBlock text)
        {
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
            double top = Math.Round((size.Height / 2 - NumeralCenter(text)) * dpi) / dpi;
            text.Arrange(new Rect(0, top, size.Width, text.DesiredSize.Height));
            return size;
        }
        return base.ArrangeOverride(size);
    }
}
