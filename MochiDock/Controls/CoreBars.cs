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
/// The Stats CPU page's per-core load, drawn as a Task-Manager-like grid in whole device pixels (<see cref="CoreBarLayout.Grid"/>):
/// many threads wrap into rows (32 as 2 x 16) instead of hair-thin bars, every cell the same size with the same gap, a track behind
/// each and the load rising from the cell's bottom in the core's colour, rounded only as much as a small cell allows.
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

    /// <summary>The preferred gap between cells in DIPs (whole device pixels after scaling).</summary>
    public double Gap { get; set; } = 3;

    // The device scale the cells were last drawn for; a change (interface size, another monitor's DPI, the first layout under a
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
        var grid = CoreBarLayout.Grid(ActualWidth * sx, ActualHeight * sy, cores.Count, Gap * Math.Min(sx, sy));
        if (grid.CellWidth <= 0 || grid.CellHeight <= 0) return;
        double cellWidth = grid.CellWidth / sx, cellHeight = grid.CellHeight / sy;
        double radius = Math.Min(1.5, Math.Min(cellWidth, cellHeight) / 2);
        for (int i = 0; i < cores.Count; i++)
        {
            int row = i / grid.Columns, column = i % grid.Columns;
            double x = (grid.Left + column * (grid.CellWidth + grid.Gap)) / sx;
            double top = (grid.Top + row * (grid.CellHeight + grid.Gap)) / sy, bottom = top + cellHeight;
            // The load rises from the cell's bottom in whole device pixels, at least one so an idle thread still shows its colour.
            int fillPixels = Math.Clamp((int)Math.Round(Math.Clamp(cores[i].Usage, 0, 100) / 100 * grid.CellHeight), 1, grid.CellHeight);
            double fill = fillPixels / sy, fillRadius = Math.Min(radius, fill / 2);
            // Guidelines snap every edge to device pixels, so at fractional scales (125 %) no cell picks up an anti-aliased extra pixel.
            dc.PushGuidelineSet(new GuidelineSet(new[] { x, x + cellWidth }, new[] { top, bottom - fill, bottom }));
            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(x, top, cellWidth, cellHeight), radius, radius);
            dc.DrawRoundedRectangle(cores[i].CoreBrush, null, new Rect(x, bottom - fill, cellWidth, fill), fillRadius, fillRadius);
            dc.Pop();
        }
    }
}
