using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MochiDock.Core;

namespace MochiDock.Views.Cards;

/// <summary>
/// The Battery page's Power chart: total power from 0 W against a round ceiling, stacked as other / CPU / GPU on a desktop
/// (one area for a laptop battery rate), with a dashed average line and a dot on the latest peak. Geometry comes from
/// <see cref="PowerChart.Build"/>; every brush is a theme resource reference, so a theme switch needs no redraw.
/// Redraws on <see cref="Show"/> and when its size changes.
/// </summary>
public sealed class PowerChartView : Grid
{
    readonly Canvas plot = new() { ClipToBounds = true };
    readonly Rectangle midLine = new() { Height = 1 };
    readonly Polygon other = new(), cpu = new(), gpu = new();
    readonly Polyline total = new() { StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round };
    readonly Line average = new() { StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } };
    readonly Ellipse peak = new() { Width = 5, Height = 5 };
    readonly TextBlock ceilingLabel = new() { FontSize = 10, Margin = new Thickness(5, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    readonly TextBlock empty = new() { FontSize = 11, Text = "Collecting power readings…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    IReadOnlyList<PowerSample> samples = Array.Empty<PowerSample>();
    int capacity = 60;

    /// <summary>The geometry last drawn (null while there is too little history); for tests and harnesses.</summary>
    public PowerChartGeometry? Geometry { get; private set; }

    public PowerChartView()
    {
        ClipToBounds = true;
        midLine.SetResourceReference(Shape.FillProperty, "IslandChartGridBrush");
        other.SetResourceReference(Shape.FillProperty, "IslandMutedInkBrush");
        cpu.SetResourceReference(Shape.FillProperty, "IslandGreenBrush");
        gpu.SetResourceReference(Shape.FillProperty, "IslandBlueBrush");
        cpu.Opacity = 0.55;
        gpu.Opacity = 0.6;
        total.SetResourceReference(Shape.StrokeProperty, "IslandInkBrush");
        average.SetResourceReference(Shape.StrokeProperty, "IslandSecondaryInkBrush");
        peak.SetResourceReference(Shape.FillProperty, "IslandInkBrush");
        ceilingLabel.SetResourceReference(TextBlock.ForegroundProperty, "IslandMutedInkBrush");
        empty.SetResourceReference(TextBlock.ForegroundProperty, "IslandMutedInkBrush");
        System.Windows.Documents.Typography.SetNumeralAlignment(ceilingLabel, FontNumeralAlignment.Tabular);
        foreach (UIElement shape in new UIElement[] { midLine, other, cpu, gpu, total, average, peak }) plot.Children.Add(shape);
        Children.Add(plot);
        Children.Add(ceilingLabel);
        Children.Add(empty);
        SizeChanged += (_, _) => Redraw();
    }

    /// <summary>Draws <paramref name="history"/> (oldest first) in <paramref name="slots"/> fixed slots, newest at the right edge.</summary>
    public void Show(IReadOnlyList<PowerSample> history, int slots)
    {
        samples = history;
        capacity = Math.Max(2, slots);
        Redraw();
    }

    void Redraw()
    {
        double width = ActualWidth, height = ActualHeight;
        Geometry = PowerChart.Build(samples, capacity, width, height);
        var g = Geometry;
        bool drawn = g != null;
        plot.Visibility = ceilingLabel.Visibility = drawn ? Visibility.Visible : Visibility.Collapsed;
        empty.Visibility = drawn ? Visibility.Collapsed : Visibility.Visible;
        if (g == null) return;

        // A laptop battery rate has no parts: its single area uses the accent instead of the muted "other" colour.
        other.SetResourceReference(Shape.FillProperty, g.HasBreakdown ? "IslandMutedInkBrush" : "IslandAccentBrush");
        other.Opacity = g.HasBreakdown ? 0.28 : 0.4;
        Fill(other, g.OtherArea);
        Fill(cpu, g.CpuArea);
        Fill(gpu, g.GpuArea);
        total.Points = Points(g.TotalLine);

        midLine.Width = width;
        Canvas.SetTop(midLine, Math.Round(height - (height - 2) / 2));
        average.X1 = 0; average.X2 = width; average.Y1 = average.Y2 = Math.Round(g.AverageY) + 0.5;
        Canvas.SetLeft(peak, g.PeakPoint.X - peak.Width / 2);
        Canvas.SetTop(peak, g.PeakPoint.Y - peak.Height / 2);
        ceilingLabel.Text = g.CeilingText;
    }

    static void Fill(Polygon polygon, IReadOnlyList<ChartPoint> points)
    {
        polygon.Points = Points(points);
        polygon.Visibility = points.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Frozen: a fresh collection per redraw, never shared with another thread.
    static PointCollection Points(IReadOnlyList<ChartPoint> points)
    {
        var collection = new PointCollection(points.Count);
        foreach (var p in points) collection.Add(new Point(p.X, p.Y));
        collection.Freeze();
        return collection;
    }
}
