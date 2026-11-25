using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MochiDock.Core;
using Size = System.Windows.Size;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Control = System.Windows.Controls.Control;

namespace MochiDock.Controls;

/// <summary>
/// Measures and arranges the resting capsule <c>[Mochi] clock [weather]</c> (the <c>PillNormalContent</c> grid of MainWindow):
/// either with the clock exactly centred and both items at the edges, or with the same space before, between and after the three items (<see cref="CapsuleClockLayout"/>).
/// Shared by MainWindow and the render harness so both run the same code.
/// </summary>
public static class CapsuleClockWeatherArranger
{
    static readonly TextBlock MeasureText = new();

    /// <summary>The layout for the current clock text and weather badge, or null when the badge is not laid out yet.</summary>
    public static CapsuleClockLayoutResult? Measure(Control clock, string clockText, FrameworkElement mascot, FrameworkElement weatherBadge, double border,
        CapsuleClockAlignment alignment)
    {
        // Same face as the clock, with tabular digits so the width does not change from minute to minute.
        Typography.SetNumeralAlignment(MeasureText, FontNumeralAlignment.Tabular);
        MeasureText.FontFamily = clock.FontFamily;
        MeasureText.FontSize = clock.FontSize;
        MeasureText.FontWeight = clock.FontWeight;
        MeasureText.FontStyle = clock.FontStyle;
        MeasureText.FontStretch = clock.FontStretch;
        MeasureText.Text = clockText;
        MeasureText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        weatherBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double weatherWidth = weatherBadge.DesiredSize.Width;
        if (weatherWidth <= 0) return null; // collapsed until its trigger applies

        double rightWidth = weatherWidth;
        if (weatherBadge.Parent is Panel parentPanel)
        {
            parentPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double groupWidth = parentPanel.DesiredSize.Width - parentPanel.Margin.Left - parentPanel.Margin.Right;
            if (groupWidth > rightWidth) rightWidth = groupWidth;
        }

        double leftWidth;
        if (double.IsFinite(mascot.Width) && mascot.Width > 0)
        {
            leftWidth = mascot.Width;
        }
        else
        {
            mascot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            leftWidth = mascot.DesiredSize.Width;
        }

        return CapsuleClockLayout.Compute(MeasureText.DesiredSize.Width, leftWidth, rightWidth, alignment, border: border);
    }

    public static void Apply(CapsuleClockLayoutResult layout, ColumnDefinition left, ColumnDefinition right,
        FrameworkElement mascot, FrameworkElement rightAnchors, FrameworkElement weatherBadge, FrameworkElement ticker)
    {
        left.Width = new GridLength(layout.LeftColumn);
        right.Width = new GridLength(layout.RightColumn);
        ticker.Margin = new Thickness(0);
        // Both modes: Mochi Edge from the left rim, the badge Edge from the right rim; the column widths decide the rest.
        mascot.HorizontalAlignment = HorizontalAlignment.Left;
        mascot.Margin = new Thickness(layout.Edge, 0, 0, 0);
        // The badge sits inside a StackPanel with its own right margin; subtract it so the badge itself ends Edge from the rim.
        double inner = (weatherBadge.Parent as FrameworkElement)?.Margin.Right ?? 0;
        rightAnchors.HorizontalAlignment = HorizontalAlignment.Right;
        rightAnchors.Margin = new Thickness(0, 0, Math.Max(0, layout.Edge - inner), 0);
    }

    /// <summary>Hands the elements back to their XAML: style-driven values are cleared, attribute values are put back as written.</summary>
    public static void Reset(ColumnDefinition left, ColumnDefinition right, FrameworkElement mascot, FrameworkElement rightAnchors, FrameworkElement ticker)
    {
        left.ClearValue(ColumnDefinition.WidthProperty);
        right.ClearValue(ColumnDefinition.WidthProperty);
        mascot.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
        mascot.ClearValue(FrameworkElement.MarginProperty);
        rightAnchors.HorizontalAlignment = HorizontalAlignment.Center;
        rightAnchors.ClearValue(FrameworkElement.MarginProperty);
        ticker.Margin = new Thickness(4, 0, 4, 0);
    }
}
