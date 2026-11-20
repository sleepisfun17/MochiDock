using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MochiDock.Services;
using MochiDock.Models;
using TextBlock = System.Windows.Controls.TextBlock;
using Size = System.Windows.Size;
namespace MochiDock.Controls;

// Stable number slots keep the capsule still while telemetry changes.
public sealed class GamingReadout : StackPanel
{
    readonly TextBlock fps, gpu, temperature, cpu, weather;
    readonly FrameworkElement temperatureGroup, weatherGroup;
    readonly ContentControl weatherIcon;
    // Slots only ever grow during a game so the capsule never jitters: FPS keeps its widest digit count, the temperature stays once shown.
    int fpsDigits = 3;
    bool temperatureSeen, temperatureWide;
    public GamingReadout()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        fps = Number(12); Children.Add(Line(fps)); Children.Add(Line(Label("FPS"))); Divider();
        Children.Add(Line(Label("GPU"))); gpu = Number(12); Children.Add(Line(gpu));
        var temp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        temp.Children.Add(Line(Label("·"))); temperature = Number(12); temp.Children.Add(Line(temperature));
        temperatureGroup = temp; Children.Add(temp); Divider();
        Children.Add(Line(Label("CPU"))); cpu = Number(12); Children.Add(Line(cpu));
        var extra = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        weatherIcon = new ContentControl { Width=14, Height=14, Margin=new Thickness(0,0,4,0), VerticalAlignment=VerticalAlignment.Center };
        var icons = new ResourceDictionary { Source = new Uri("/MochiDock;component/Views/WeatherIconResources.xaml",UriKind.Relative) };
        weatherIcon.ContentTemplate=(DataTemplate)icons["WeatherVectorIconTemplate"];
        extra.Children.Add(weatherIcon);
        weather = Number(12); weather.SetResourceReference(TextBlock.FontWeightProperty,"IslandSmallLabelWeight"); extra.Children.Add(Line(weather));
        var badge = new Border { Child=extra, CornerRadius=new CornerRadius(7), Padding=new Thickness(6,2,6,2), Margin=new Thickness(10,0,0,0), VerticalAlignment=VerticalAlignment.Center };
        badge.SetResourceReference(Border.BackgroundProperty,"IslandChipBackgroundBrush");
        weatherGroup = badge; Children.Add(badge);
    }
    static CompactTextLine Line(TextBlock text) => new() { Child=text, VerticalAlignment=VerticalAlignment.Stretch };
    static TextBlock Label(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 12, FontWeight=FontWeights.Medium, Margin = new Thickness(3,0,3,0), VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty,"IslandSecondaryInkBrush"); return block;
    }
    static TextBlock Number(double size)
    {
        var block = new TextBlock { FontSize = size, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        block.SetResourceReference(TextBlock.ForegroundProperty,"IslandInkBrush");
        block.SetResourceReference(TextBlock.FontWeightProperty,"IslandPrimaryReadingWeight");
        block.Typography.NumeralAlignment = FontNumeralAlignment.Tabular; return block;
    }
    void Divider()
    {
        var border = new Border { Width=1, Height=14, Margin=new Thickness(7,0,7,0) };
        border.SetResourceReference(Border.BackgroundProperty,"IslandControlBorderBrush"); Children.Add(border);
    }
    static void Slot(TextBlock block,string text,string capacity)
    {
        var probe = new TextBlock { Text=capacity, FontFamily=block.FontFamily, FontSize=block.FontSize, FontWeight=block.FontWeight };
        probe.Typography.NumeralAlignment=FontNumeralAlignment.Tabular;
        probe.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        block.Width=Math.Ceiling(probe.DesiredSize.Width); block.Text=text;
    }
    /// <summary>Forget the widths remembered during a game (call when the gaming readout stops being shown).</summary>
    public void Reset(){fpsDigits=3;temperatureSeen=false;temperatureWide=false;}
    public void Update(HardwareStats stats,WeatherInfo? weatherInfo)
    {
        fpsDigits=Math.Max(fpsDigits,stats.CurrentFps.ToString(CultureInfo.InvariantCulture).Length);
        Slot(fps,stats.CurrentFps>0?stats.CurrentFps.ToString(CultureInfo.InvariantCulture):"—",new string('0',fpsDigits));
        Slot(gpu,$"{stats.GpuUsage:0}%","100%"); Slot(cpu,$"{stats.CpuUsage:0}%","100%");
        bool temperatureValid=stats.GpuTemp>0&&stats.IsGpuTempMeasured;
        temperatureSeen|=temperatureValid;
        // A reading that drops out for a moment keeps its slot ("—°") instead of collapsing and shifting everything.
        temperatureGroup.Visibility=temperatureSeen?Visibility.Visible:Visibility.Collapsed;
        temperatureWide|=stats.GpuTemp>=100;
        Slot(temperature,temperatureValid?$"{stats.GpuTemp:0}°":"—°",temperatureWide?"100°":"00°");
        weatherIcon.Content=weatherInfo;
        // Fixed slot for "-00°C": the badge keeps its width whatever the outdoor temperature is.
        if(weatherInfo is null)weather.Text="";else Slot(weather,$"{weatherInfo.Temperature:0}°C","-00°C");
        weatherGroup.Visibility=weatherInfo is null?Visibility.Collapsed:Visibility.Visible;
    }
}
