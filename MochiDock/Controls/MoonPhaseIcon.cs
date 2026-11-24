using System;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace MochiDock.Controls;

/// <summary>Renders the current lunar phase continuously with theme brushes and crisp vector geometry.</summary>
public class MoonPhaseIcon : FrameworkElement
{
    public static readonly DependencyProperty CycleFractionProperty = DependencyProperty.Register(
        nameof(CycleFraction), typeof(double), typeof(MoonPhaseIcon),
        new FrameworkPropertyMetadata(0.38, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SouthernHemisphereProperty = DependencyProperty.Register(
        nameof(SouthernHemisphere), typeof(bool), typeof(MoonPhaseIcon),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LitBrushProperty = DependencyProperty.Register(
        nameof(LitBrush), typeof(Brush), typeof(MoonPhaseIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShadowBrushProperty = DependencyProperty.Register(
        nameof(ShadowBrush), typeof(Brush), typeof(MoonPhaseIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OutlineBrushProperty = DependencyProperty.Register(
        nameof(OutlineBrush), typeof(Brush), typeof(MoonPhaseIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double CycleFraction
    {
        get => (double)GetValue(CycleFractionProperty);
        set => SetValue(CycleFractionProperty, value);
    }

    public bool SouthernHemisphere
    {
        get => (bool)GetValue(SouthernHemisphereProperty);
        set => SetValue(SouthernHemisphereProperty, value);
    }

    public Brush? LitBrush
    {
        get => (Brush?)GetValue(LitBrushProperty);
        set => SetValue(LitBrushProperty, value);
    }

    public Brush? ShadowBrush
    {
        get => (Brush?)GetValue(ShadowBrushProperty);
        set => SetValue(ShadowBrushProperty, value);
    }

    public Brush? OutlineBrush
    {
        get => (Brush?)GetValue(OutlineBrushProperty);
        set => SetValue(OutlineBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 2 || h <= 2) return;

        double r = Math.Min(w, h) / 2.0 - 0.75;
        if (r <= 1) return;
        double cx = w / 2.0, cy = h / 2.0;

        var lit = LitBrush ?? TryFindResource("IslandWarningBrush") as Brush ?? Brushes.Gold;
        var shadow = ShadowBrush ?? TryFindResource("IslandControlBackgroundBrush") as Brush ?? Brushes.Transparent;
        var outline = OutlineBrush ?? TryFindResource("IslandMutedInkBrush") as Brush ?? Brushes.Gray;

        var disc = new EllipseGeometry(new Point(cx, cy), r, r);
        dc.DrawEllipse(shadow, null, new Point(cx, cy), r, r);

        double raw = CycleFraction;
        double f = double.IsFinite(raw) ? ((raw % 1.0) + 1.0) % 1.0 : 0.0;

        if (Math.Abs(f - 0.5) <= 0.015)
        {
            dc.DrawEllipse(lit, null, new Point(cx, cy), r, r);
        }
        else if (f > 0.015 && f < 0.985)
        {
            dc.PushClip(disc);
            bool litRight = SouthernHemisphere ? f >= 0.5 : f < 0.5;
            double k = Math.Cos(f * 2.0 * Math.PI); // +1 near New Moon, 0 at Quarter, -1 near Full Moon
            double rx = Math.Max(0.01, Math.Abs(k) * r);

            var outerSweep = litRight ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
            var innerSweep = (litRight == (k >= 0)) ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;

            var top = new Point(cx, cy - r);
            var bottom = new Point(cx, cy + r);
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(top, isFilled: true, isClosed: true);
                ctx.ArcTo(bottom, new Size(r, r), 0, isLargeArc: false, outerSweep, isStroked: false, isSmoothJoin: false);
                ctx.ArcTo(top, new Size(rx, r), 0, isLargeArc: false, innerSweep, isStroked: false, isSmoothJoin: false);
            }
            geo.Freeze();
            dc.DrawGeometry(lit, null, geo);
            dc.Pop();
        }

        var pen = new Pen(outline, 1.0);
        pen.Freeze();
        dc.DrawEllipse(null, pen, new Point(cx, cy), r, r);
    }
}
