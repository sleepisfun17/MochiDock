using System.Windows;
using System.Windows.Controls;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace MochiDock.Controls;

/// <summary>
/// A vertical stack whose last visible child takes all the height that is left, so a Stats column ends at the page's bottom
/// edge instead of leaving an empty band (the other children keep their own height, as in a StackPanel).
/// </summary>
public sealed class FillLastPanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double width = 0, height = 0;
        var last = LastVisible();
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            double room = double.IsInfinity(available.Height) ? double.PositiveInfinity : Math.Max(0, available.Height - height);
            child.Measure(new Size(available.Width, ReferenceEquals(child, last) ? room : double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0;
        var last = LastVisible();
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            double h = ReferenceEquals(child, last) ? Math.Max(child.DesiredSize.Height, final.Height - y) : child.DesiredSize.Height;
            child.Arrange(new Rect(0, y, final.Width, h));
            y += h;
        }
        return final;
    }

    private UIElement? LastVisible()
    {
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
            if (InternalChildren[i] is { Visibility: not Visibility.Collapsed } child) return child;
        return null;
    }
}
