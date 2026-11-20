using System.Windows;
using System.Windows.Controls;
using Binding = System.Windows.Data.Binding;
using System.Windows.Media;
using TextBlock = System.Windows.Controls.TextBlock;
using Button = System.Windows.Controls.Button;
using Size = System.Windows.Size;

namespace MochiDock.Controls;

// One measured row; the weather slot and its gap disappear together.
public sealed class PrayerReminderReadout : StackPanel
{
    public static readonly DependencyProperty PrayerNameProperty = DependencyProperty.Register(
        nameof(PrayerName), typeof(string), typeof(PrayerReminderReadout),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty ShowWeatherProperty = DependencyProperty.Register(
        nameof(ShowWeather), typeof(bool), typeof(PrayerReminderReadout),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure,
            (d, e) => ((PrayerReminderReadout)d).weather.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));
    public string PrayerName { get => (string)GetValue(PrayerNameProperty); set => SetValue(PrayerNameProperty, value); }
    public bool ShowWeather { get => (bool)GetValue(ShowWeatherProperty); set => SetValue(ShowWeatherProperty, value); }
    public bool IsAcknowledgeHovered => _button.IsMouseOver;
    public event RoutedEventHandler? Acknowledged;

    readonly TextBlock label;
    readonly Button _button;
    readonly StackPanel weather;
    string slotKey = "";

    public PrayerReminderReadout()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal;
        NameScope.SetNameScope(this, new NameScope());
        SetBinding(PrayerNameProperty, new Binding("CurrentPrayerSchedule.NextPrayer.Name"));
        label = new TextBlock { FontSize = 13 };
        label.SetResourceReference(TextBlock.FontFamilyProperty, "IslandUiFont");
        label.SetResourceReference(TextBlock.FontWeightProperty, "IslandPrimaryReadingWeight");
        label.SetResourceReference(TextBlock.ForegroundProperty, "IslandInkBrush");
        label.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
        label.SetBinding(TextBlock.TextProperty, new Binding("PrayerReminderText"));
        RegisterName("PrayerReminderLabel", label);
        Children.Add(new CompactTextLine { Child = label });
        _button = new Button { Content = "OK", Width = 40, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _button.SetResourceReference(Button.StyleProperty, "ActionButton");
        _button.Click += (_, e) => Acknowledged?.Invoke(this, e);
        RegisterName("PrayerReminderButton", _button);
        Children.Add(_button);
        weather = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        RegisterName("PrayerWeather", weather);
        var icon = new ContentControl { Width = 17, Height = 17, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        var icons = new ResourceDictionary { Source = new Uri("/MochiDock;component/Views/WeatherIconResources.xaml", UriKind.Relative) };
        icon.ContentTemplate = (DataTemplate)icons["WeatherVectorIconTemplate"];
        icon.SetBinding(ContentControl.ContentProperty, new Binding("WeatherInfo"));
        weather.Children.Add(icon);
        var temperature = new TextBlock { FontSize = 11.5 };
        temperature.SetResourceReference(TextBlock.FontFamilyProperty, "IslandUiFont");
        temperature.SetResourceReference(TextBlock.FontWeightProperty, "IslandSmallLabelWeight");
        temperature.SetResourceReference(TextBlock.ForegroundProperty, "IslandInkBrush");
        temperature.SetBinding(TextBlock.TextProperty, new Binding("WeatherTemperatureBadge"));
        RegisterName("PrayerTemperature", temperature);
        weather.Children.Add(new CompactTextLine { Child = temperature });
        Children.Add(weather);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        string activePrayer = !string.IsNullOrWhiteSpace(PrayerName)
            ? PrayerName
            : (label.Text?.Split(' ').FirstOrDefault() ?? "");
        string key = $"{activePrayer}|{label.FontFamily.Source}|{label.FontSize}|{label.FontWeight}|{label.FontStyle}|{label.FontStretch}|{dpi}|{label.FlowDirection}|{TextOptions.GetTextFormattingMode(label)}";
        if (key != slotKey)
        {
            var probe = new TextBlock { FontFamily = label.FontFamily, FontSize = label.FontSize,
                FontWeight = label.FontWeight, FontStyle = label.FontStyle, FontStretch = label.FontStretch, FlowDirection = label.FlowDirection };
            VisualTreeHelper.SetRootDpi(probe, VisualTreeHelper.GetDpi(this));
            TextOptions.SetTextFormattingMode(probe, TextOptions.GetTextFormattingMode(label));
            probe.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
            double width = 0;
            for (int minute = 1; minute <= 10; minute++)
            {
                probe.Text = $"{activePrayer} {minute} min";
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                width = Math.Max(width, probe.DesiredSize.Width);
            }
            label.Width = Math.Ceiling(width);
            slotKey = key;
        }
        return base.MeasureOverride(constraint);
    }

    // Matches the normal pill's unchanged 48-DIP Mochi anchor; DesiredSize includes end margins.
    public double MeasureCapsuleWidth()
    {
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        // The row is centred right of the 48-DIP mascot anchor. Without the weather slot (which has its own 8-DIP margins)
        // the OK button would touch the capsule's right edge, so leave at least 7 DIPs (8 with rounding slack) on each side of the centred content.
        double edge = weather.Visibility == Visibility.Visible ? 0 : 16;
        return Math.Ceiling((48 + DesiredSize.Width + edge) * dpi) / dpi;
    }
}
