using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace MochiDock.Controls;

public static class TabLabelMotion
{
    static readonly DependencyPropertyDescriptor[] Metrics = new[] { TextBlock.FontFamilyProperty, TextBlock.FontWeightProperty, TextBlock.FontSizeProperty, TextBlock.TextProperty }
        .Select(p => DependencyPropertyDescriptor.FromProperty(p, typeof(TextBlock))).ToArray();
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached("Active", typeof(bool), typeof(TabLabelMotion), new PropertyMetadata(false, Changed));
    public static bool GetActive(DependencyObject d) => (bool)d.GetValue(ActiveProperty);
    public static void SetActive(DependencyObject d, bool value) => d.SetValue(ActiveProperty,value);
    static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock label) return;
        Apply(label, true);
        label.Loaded -= Loaded; label.Loaded += Loaded;
        label.Unloaded -= Unloaded; label.Unloaded += Unloaded;
        label.SizeChanged -= SizeChanged; label.SizeChanged += SizeChanged;
        if (label.IsLoaded) Watch(label, true);
    }
    static void Loaded(object sender,RoutedEventArgs e) { Watch((TextBlock)sender, true); Apply((TextBlock)sender, false); }
    static void Unloaded(object sender,RoutedEventArgs e) => Watch((TextBlock)sender, false);
    static void Watch(TextBlock label, bool enabled)
    {
        foreach (var metric in Metrics)
        {
            metric.RemoveValueChanged(label, MetricChanged);
            if (enabled) metric.AddValueChanged(label, MetricChanged);
        }
    }
    static void MetricChanged(object? sender, EventArgs e) { if (sender is TextBlock label) Apply(label, false); }
    static void SizeChanged(object sender,SizeChangedEventArgs e)
    {
        // Width changes during animation must not restart it.
        if(e.HeightChanged) Apply((TextBlock)sender, false);
    }
    static void Apply(TextBlock label, bool animate)
    {
        var text = new FormattedText(label.Text,CultureInfo.CurrentUICulture,label.FlowDirection,new Typeface(label.FontFamily,label.FontStyle,label.FontWeight,label.FontStretch),label.FontSize,System.Windows.Media.Brushes.Black,VisualTreeHelper.GetDpi(label).PixelsPerDip);
        var duration=TimeSpan.FromMilliseconds(animate&&label.IsLoaded&&SystemParameters.ClientAreaAnimation?156:0);
        if (duration == TimeSpan.Zero)
        {
            label.BeginAnimation(FrameworkElement.WidthProperty,null); label.Width=GetActive(label)?Math.Ceiling(text.WidthIncludingTrailingWhitespace):0;
            label.BeginAnimation(UIElement.OpacityProperty,null); label.Opacity=GetActive(label)?1:0;
            return;
        }
        var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
        ElementSizeAnimation.AnimateWidth(label,GetActive(label)?Math.Ceiling(text.WidthIncludingTrailingWhitespace):0,duration,ease);
        label.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(GetActive(label)?1:0,duration){EasingFunction=ease});
    }
}
