using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;

namespace MochiDock.Controls;

// Canvas contributes no desired size; highlight geometry cannot move the tabs it follows.
public sealed class HeaderSelectionOverlay : Canvas
{
    public static readonly DependencyProperty TabsProperty = DependencyProperty.Register(nameof(Tabs), typeof(Panel), typeof(HeaderSelectionOverlay));
    public Panel? Tabs { get => (Panel?)GetValue(TabsProperty); set => SetValue(TabsProperty, value); }
    public static readonly DependencyProperty IsSelectionEnabledProperty = DependencyProperty.Register(nameof(IsSelectionEnabled), typeof(bool), typeof(HeaderSelectionOverlay), new PropertyMetadata(true, (d, _) => ((HeaderSelectionOverlay)d).Refresh(false)));
    public bool IsSelectionEnabled { get => (bool)GetValue(IsSelectionEnabledProperty); set => SetValue(IsSelectionEnabledProperty, value); }
    Border? Pill => Children.OfType<Border>().FirstOrDefault();
    FrameworkElement? selected;
    Rect start;
    readonly Stopwatch clock = new();
    bool running;
    public HeaderSelectionOverlay()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { LayoutUpdated += LayoutChanged; Refresh(false); };
        Unloaded += (_, _) => { LayoutUpdated -= LayoutChanged; Cancel(); selected = null; };
        IsVisibleChanged += (_, _) => { if (!IsVisible) { Cancel(); selected = null; } else Refresh(false); };
    }
    void LayoutChanged(object? sender, EventArgs e) => Refresh(true);
    public void Cancel()
    {
        if (running) CompositionTarget.Rendering -= Frame;
        running = false; clock.Reset();
        if (Pill is { } pill) pill.Opacity = 0;
    }
    public void Refresh(bool animate)
    {
        if (Pill is not { } pill || Tabs is null) return;
        if (!IsSelectionEnabled) { Cancel(); selected = null; return; }
        if ((!animate || !SystemParameters.ClientAreaAnimation) && running) Cancel();
        var next = Tabs.Children.OfType<FrameworkElement>().FirstOrDefault(b => b.Visibility == Visibility.Visible && Equals(b.Tag, "Active"));
        if (next is null) { Cancel(); selected = null; return; }
        if (!IsVisible && IsLoaded) { Cancel(); return; }
        if (next != selected)
        {
            var canAnimate = animate && SystemParameters.ClientAreaAnimation && selected != null && pill.Opacity > 0;
            start = new Rect(Finite(GetLeft(pill)), Finite(GetTop(pill)), Finite(pill.Width), Finite(pill.Height));
            Cancel(); selected = next;
            if (canAnimate) { running = true; clock.Restart(); CompositionTarget.Rendering += Frame; }
        }
        Update();
    }
    void Frame(object? sender, EventArgs e) => Update();
    void Update()
    {
        if (Pill is not { } pill || selected is null) return;
        // Both visuals share this Canvas's parent and scroll together. TranslatePoint
        // accounts for layout offsets; ActualWidth/Height exclude external margins.
        var point = selected.TranslatePoint(new Point(), this);
        var target = new Rect(point, selected.RenderSize);
        if (!double.IsFinite(target.X) || !double.IsFinite(target.Y)) return;
        var t = running ? Math.Clamp(clock.Elapsed.TotalMilliseconds / 156, 0, 1) : 1;
        var ease = 1 - Math.Pow(1 - t, 3);
        double Mix(double a, double b) => a + (b - a) * ease;
        var x = Mix(start.X, target.X); var y = Mix(start.Y, target.Y);
        var width = Mix(start.Width, target.Width); var height = Mix(start.Height, target.Height);
        if (Math.Abs(Finite(GetLeft(pill)) - x) > .001 || double.IsNaN(GetLeft(pill))) SetLeft(pill, x);
        if (Math.Abs(Finite(GetTop(pill)) - y) > .001 || double.IsNaN(GetTop(pill))) SetTop(pill, y);
        if (!double.IsFinite(pill.Width) || Math.Abs(pill.Width - width) > .001) pill.Width = width;
        if (!double.IsFinite(pill.Height) || Math.Abs(pill.Height - height) > .001) pill.Height = height;
        pill.Opacity = 1;
        if (running && t >= 1) { CompositionTarget.Rendering -= Frame; running = false; clock.Stop(); }
    }
    static double Finite(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
}
