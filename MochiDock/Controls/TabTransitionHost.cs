using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace MochiDock.Controls;

// Existing control instances retain their local state across selection changes.
public sealed class TabTransitionHost : Grid
{
    public static readonly DependencyProperty SelectedKeyProperty = DependencyProperty.Register(nameof(SelectedKey), typeof(string), typeof(TabTransitionHost), new PropertyMetadata("", Changed));
    public string SelectedKey { get => (string)GetValue(SelectedKeyProperty); set => SetValue(SelectedKeyProperty, value); }
    FrameworkElement? current;
    long generation;
    public string Order { get; set; } = "";
    public TabTransitionHost()
    {
        ClipToBounds = true;
        Loaded += (_, _) => Select(false);
        Unloaded += (_, _) => Select(false);
        IsVisibleChanged += (_, _) => Select(false);
    }
    static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TabTransitionHost)d).Select(true);
    public void Settle() => Select(false);
    void Select(bool animate)
    {
        var pages = Children.OfType<FrameworkElement>().ToList();
        var next = pages.FirstOrDefault(c => Equals(c.Tag, SelectedKey));
        if (next is null)
        {
            ++generation;
            foreach (var page in pages) { Reset(page); page.Visibility = Visibility.Collapsed; page.IsHitTestVisible = false; }
            current = null;
            return;
        }
        var old = current;
        var version = ++generation;
        var order = Order.Split(',');
        var direction = MochiDock.Core.TabMotion.Direction(Order.Length == 0 ? pages.IndexOf(old!) : Array.IndexOf(order, old?.Tag?.ToString()), Order.Length == 0 ? pages.IndexOf(next) : Array.IndexOf(order, next.Tag?.ToString()));
        animate &= IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation && old != null && old != next;
        foreach (var c in pages)
        {
            if (animate && (c == old || c == next)) continue;
            Reset(c); c.Visibility = c == next ? Visibility.Visible : Visibility.Collapsed; c.IsHitTestVisible = c == next;
        }
        current = next;
        if (!animate) return;
        foreach (var c in new[] { old!, next })
        {
            bool incoming = c == next;
            double a = c.Visibility == Visibility.Visible ? c.Opacity : 0;
            double x = c.RenderTransform is TranslateTransform t ? t.X : incoming ? direction * 20 : 0;
            c.BeginAnimation(OpacityProperty, null); c.Opacity = a;
            c.Visibility = Visibility.Visible; c.IsHitTestVisible = incoming;
            var transform = new TranslateTransform(x, 0); c.RenderTransform = transform;
            var alpha = new DoubleAnimation(a, incoming ? 1 : 0, TimeSpan.FromMilliseconds(incoming ? 130 : 104)) { BeginTime = TimeSpan.FromMilliseconds(incoming ? 26 : 0), EasingFunction = new Smooth() };
            var slide = new DoubleAnimation(x, incoming ? 0 : -direction * 20, alpha.Duration) { BeginTime = alpha.BeginTime, EasingFunction = new Smooth() };
            alpha.Completed += (_, _) => { if (version != generation) return; Reset(c); c.Visibility = incoming ? Visibility.Visible : Visibility.Collapsed; };
            c.BeginAnimation(OpacityProperty, alpha); transform.BeginAnimation(TranslateTransform.XProperty, slide);
        }
    }
    static void Reset(FrameworkElement c) { c.BeginAnimation(OpacityProperty, null); c.Opacity = 1; c.RenderTransform = Transform.Identity; }
    sealed class Smooth : EasingFunctionBase
    {
        public Smooth() { EasingMode = EasingMode.EaseIn; }
        protected override double EaseInCore(double t) => t * t * (3 - 2 * t);
        protected override Freezable CreateInstanceCore() => new Smooth();
    }
}
