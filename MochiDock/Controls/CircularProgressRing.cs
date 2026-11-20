using System;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Controls;

public class CircularProgressRing : UserControl
{
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(CircularProgressRing),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(CircularProgressRing),
            new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressBrushProperty =
        DependencyProperty.Register(nameof(ProgressBrush), typeof(Brush), typeof(CircularProgressRing),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(CircularProgressRing),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Brush ProgressBrush
    {
        get => (Brush)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= StrokeThickness * 2) return;

        double radius = (size - StrokeThickness) / 2.0;
        Point center = new Point(ActualWidth / 2.0, ActualHeight / 2.0);

        // Background Track
        var trackPen = new Pen(TrackBrush, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawEllipse(null, trackPen, center, radius, radius);

        double pct = Math.Clamp(Progress, 0.0, 100.0);
        if (pct <= 0.001) return;

        var progressPen = new Pen(ProgressBrush, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        if (pct >= 99.99)
        {
            dc.DrawEllipse(null, progressPen, center, radius, radius);
            return;
        }

        double angle = (pct / 100.0) * 360.0;
        double startAngle = -90.0; // 12 o'clock
        double endAngle = startAngle + angle;

        Point startPoint = PointOnCircle(center, radius, startAngle);
        Point endPoint = PointOnCircle(center, radius, endAngle);
        bool isLargeArc = angle > 180.0;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(startPoint, false, false);
            ctx.ArcTo(endPoint, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true, false);
        }

        dc.DrawGeometry(null, progressPen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angleInDegrees)
    {
        double rad = angleInDegrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }
}
