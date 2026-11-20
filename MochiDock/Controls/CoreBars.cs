using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using MochiDock.Core;
using MochiDock.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace MochiDock.Controls;

/// <summary>
/// The Stats CPU page's per-core bars, drawn in whole device pixels (<see cref="CoreBarLayout"/>): every bar the same width with the
/// same gap, a track behind each and the load in the core's colour, rounded only as much as a thin bar allows.
/// </summary>
public sealed class CoreBars : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items), typeof(IEnumerable), typeof(CoreBars),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((CoreBars)d).OnItemsChanged(e.OldValue, e.NewValue)));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(CoreBars),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The cores (<see cref="CoreUsageItem"/>): their load and their colour.</summary>
    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }

    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    /// <summary>The preferred gap between bars in DIPs (whole device pixels after scaling).</summary>
    public double Gap { get; set; } = 3;

    // The device scale the bars were last drawn for; a change (interface size, another monitor's DPI, the first layout under a
    // zoom) redraws them so they stay whole pixels.
    private (double X, double Y) _drawnScale;

    public CoreBars()
    {
        SetResourceReference(TrackBrushProperty, "IslandTrackBrush");
        SnapsToDevicePixels = true;
        LayoutUpdated += (_, _) => { if (IsVisible && DeviceScale() != _drawnScale) InvalidateVisual(); };
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    private void OnItemsChanged(object? oldValue, object? newValue)
    {
        if (oldValue is INotifyCollectionChanged oldList) oldList.CollectionChanged -= ItemsCollectionChanged;
        if (newValue is INotifyCollectionChanged newList) newList.CollectionChanged += ItemsCollectionChanged;
    }

    private void ItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    // Device pixels per DIP for this element: the monitor's DPI times every scale above it (MochiDock's interface size zoom is a
    // layout transform, which GetDpi alone does not include).
    private (double X, double Y) DeviceScale()
    {
        try
        {
            if (PresentationSource.FromVisual(this) is { CompositionTarget: { } target, RootVisual: { } root } && TransformToAncestor(root) is Transform toRoot)
            {
                var m = toRoot.Value * target.TransformToDevice;
                double x = Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12), y = Math.Sqrt(m.M21 * m.M21 + m.M22 * m.M22);
                if (x > 0 && y > 0) return (x, y);
            }
        }
        catch (InvalidOperationException) { }   // not connected to a root yet
        var dpi = VisualTreeHelper.GetDpi(this);
        return (dpi.DpiScaleX, dpi.DpiScaleY);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var cores = Items?.OfType<CoreUsageItem>().ToList();
        if (cores is not { Count: > 0 } || ActualWidth <= 0 || ActualHeight <= 0) return;
        var (sx, sy) = _drawnScale = DeviceScale();
        var (bar, gap, left) = CoreBarLayout.Compute(ActualWidth * sx, cores.Count, Gap * sx);
        if (bar <= 0) return;
        double barWidth = bar / sx, height = Math.Round(ActualHeight * sy) / sy, radius = Math.Min(1.5, barWidth / 2);
        for (int i = 0; i < cores.Count; i++)
        {
            double x = (left + i * (bar + gap)) / sx;
            double fill = Math.Round(Math.Clamp(cores[i].HeightValue / 46.0, 0, 1) * height * sy) / sy;
            // Guidelines snap both edges to device pixels, so at fractional scales (125 %) no bar picks up an anti-aliased extra pixel.
            dc.PushGuidelineSet(new GuidelineSet(new[] { x, x + barWidth }, new[] { 0, height - fill, height }));
            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(x, 0, barWidth, height), radius, radius);
            if (fill > 0) dc.DrawRoundedRectangle(cores[i].CoreBrush, null, new Rect(x, height - fill, barWidth, fill), radius, radius);
            dc.Pop();
        }
    }
}
