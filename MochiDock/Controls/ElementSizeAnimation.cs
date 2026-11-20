using System.Windows;
using System.Windows.Media.Animation;

namespace MochiDock.Controls;

public static class ElementSizeAnimation
{
    public static void AnimateWidth(FrameworkElement element, double target, TimeSpan duration, IEasingFunction? easing = null)
    {
        // Width defaults to Auto (NaN), which a To-only animation cannot interpolate.
        // Read the current animated value before replacing its clock so retargeting stays continuous.
        var current = element.Width;
        if (!double.IsFinite(current)) current = element.ActualWidth;
        if (!double.IsFinite(current) || current < 0) current = 0;
        if (!double.IsFinite(target) || target < 0) throw new ArgumentOutOfRangeException(nameof(target));

        element.BeginAnimation(FrameworkElement.WidthProperty, null);
        // These presentation widths are owned here (not data bound). A local base value
        // must remain finite even before the first tick and after FillBehavior.Stop.
        element.Width = target;
        if (duration <= TimeSpan.Zero) return;
        element.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(current, target, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        });
    }
}
