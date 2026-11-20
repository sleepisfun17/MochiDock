using System.Windows;
using System.Windows.Media;
using Border = System.Windows.Controls.Border;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Pen = System.Windows.Media.Pen;

namespace MochiDock.Controls;

// The shoulder extends outside the body's layout box; content keeps its existing coordinates.
public sealed class IslandChrome : Border
{
    public static readonly DependencyProperty IsNotchProperty = DependencyProperty.Register(
        nameof(IsNotch), typeof(bool), typeof(IslandChrome), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender,
            (d,_) => ((IslandChrome)d).RefreshClip()));
    public bool IsNotch { get => (bool)GetValue(IsNotchProperty); set => SetValue(IsNotchProperty, value); }
    void RefreshClip() => Clip = IsNotch && ActualWidth > 0 && ActualHeight > 0 ? NotchGeometry() : null;
    protected override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); RefreshClip(); }

    public Geometry NotchGeometry()
    {
        double w = ActualWidth, h = ActualHeight, s = Math.Min(9,h), r = Math.Min(20,Math.Min(w/2,Math.Max(0,h-s)));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(-s,0),true,false);
            context.ArcTo(new Point(0,s),new Size(s,s),0,false,SweepDirection.Clockwise,true,false);
            context.LineTo(new Point(0,h-r),true,false);
            context.ArcTo(new Point(r,h),new Size(r,r),0,false,SweepDirection.Counterclockwise,true,false);
            context.LineTo(new Point(w-r,h),true,false);
            context.ArcTo(new Point(w,h-r),new Size(r,r),0,false,SweepDirection.Counterclockwise,true,false);
            context.LineTo(new Point(w,s),true,false);
            context.ArcTo(new Point(w+s,0),new Size(s,s),0,false,SweepDirection.Clockwise,true,false);
            // Fill closes across the screen edge; the outline deliberately does not.
        }
        geometry.Freeze(); return geometry;
    }
    protected override void OnRender(DrawingContext context)
    {
        if (!IsNotch) { base.OnRender(context); return; }
        if (ActualWidth<=0 || ActualHeight<=0) return;
        var pen = BorderBrush is null ? null : new Pen(BorderBrush,BorderThickness.Left);
        context.DrawGeometry(Background,pen,NotchGeometry());
    }
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) => IsNotch
        ? (NotchGeometry().FillContains(parameters.HitPoint) ? new PointHitTestResult(this,parameters.HitPoint) : null)
        : base.HitTestCore(parameters);
}
