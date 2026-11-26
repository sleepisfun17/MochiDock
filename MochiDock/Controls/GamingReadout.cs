using System.Windows;
using System.Windows.Controls;
using MochiDock.Core;
using MochiDock.Models;
using MochiDock.Services;
using TextBlock = System.Windows.Controls.TextBlock;
using Size = System.Windows.Size;

namespace MochiDock.Controls;

// Stable number slots keep the capsule still while telemetry changes.
public sealed class GamingReadout : StackPanel
{
    readonly TextBlock fps, gpu, temperature, cpu, cpuTemperature, weather;
    readonly FrameworkElement temperatureGroup, cpuTemperatureGroup, weatherGroup;
    readonly ContentControl weatherIcon;
    readonly GamingReadoutState state = new();
    TemperatureSeverity gpuSeverity = TemperatureSeverity.Normal;
    TemperatureSeverity cpuSeverity = TemperatureSeverity.Normal;

    public GamingReadout()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;

        fps = Number(12);
        Children.Add(Line(fps));
        Children.Add(Line(Label("FPS")));
        Children.Add(CreateDivider());

        Children.Add(Line(Label("GPU")));
        gpu = Number(12);
        Children.Add(Line(gpu));
        temperature = Number(12);
        temperatureGroup = BuildTemperatureGroup(temperature);
        Children.Add(temperatureGroup);
        Children.Add(CreateDivider());

        Children.Add(Line(Label("CPU")));
        cpu = Number(12);
        Children.Add(Line(cpu));
        cpuTemperature = Number(12);
        cpuTemperatureGroup = BuildTemperatureGroup(cpuTemperature);
        Children.Add(cpuTemperatureGroup);

        var extra = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        extra.Children.Add(CreateDivider());
        weatherIcon = new ContentControl { Width = 15, Height = 15, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
        var icons = new ResourceDictionary { Source = new Uri("/MochiDock;component/Views/WeatherIconResources.xaml", UriKind.Relative) };
        weatherIcon.ContentTemplate = (DataTemplate)icons["WeatherVectorIconTemplate"];
        extra.Children.Add(weatherIcon);
        weather = Number(12);
        weather.TextAlignment = TextAlignment.Left;
        weather.SetResourceReference(TextBlock.FontWeightProperty, "IslandSmallLabelWeight");
        extra.Children.Add(Line(weather));
        weatherGroup = extra;
        Children.Add(extra);
    }

    static StackPanel BuildTemperatureGroup(TextBlock reading)
    {
        var group = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        group.Children.Add(Line(Label("·")));
        group.Children.Add(Line(reading));
        return group;
    }

    static Border CreateDivider()
    {
        var border = new Border { Width = 1, Height = 14, Margin = new Thickness(7, 0, 7, 0) };
        border.SetResourceReference(Border.BackgroundProperty, "IslandControlBorderBrush");
        return border;
    }

    static CompactTextLine Line(TextBlock text) => new() { Child = text, VerticalAlignment = VerticalAlignment.Stretch };

    static TextBlock Label(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "IslandSecondaryInkBrush");
        return block;
    }

    static TextBlock Number(double size)
    {
        var block = new TextBlock { FontSize = size, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        block.SetResourceReference(TextBlock.ForegroundProperty, "IslandInkBrush");
        block.SetResourceReference(TextBlock.FontWeightProperty, "IslandPrimaryReadingWeight");
        block.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
        return block;
    }

    static void Slot(TextBlock block, string text, string capacity)
    {
        var probe = new TextBlock { Text = capacity, FontFamily = block.FontFamily, FontSize = block.FontSize, FontWeight = block.FontWeight };
        probe.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        block.Width = Math.Ceiling(probe.DesiredSize.Width);
        block.Text = text;
    }

    static void ApplyTemperatureBrush(TextBlock target, ref TemperatureSeverity current, TemperatureSeverity next)
    {
        if (current == next) return;
        current = next;
        string brushKey = next switch
        {
            TemperatureSeverity.Hot => "IslandRedBrush",
            TemperatureSeverity.Warm => "IslandOrangeBrush",
            _ => "IslandInkBrush",
        };
        target.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
    }

    /// <summary>Forget the widths remembered during a game (call when the gaming readout stops being shown).</summary>
    public void Reset()
    {
        state.Reset();
        ApplyTemperatureBrush(temperature, ref gpuSeverity, TemperatureSeverity.Normal);
        ApplyTemperatureBrush(cpuTemperature, ref cpuSeverity, TemperatureSeverity.Normal);
    }

    public void Update(HardwareStats stats, WeatherInfo? weatherInfo, TemperatureThresholds thresholds)
    {
        var snap = state.Update(
            stats.CurrentFps,
            stats.GpuTemp, stats.IsGpuTempMeasured,
            stats.CpuTemp, stats.IsCpuTempMeasured,
            thresholds);

        Slot(fps, snap.FpsText, snap.FpsCapacity);
        Slot(gpu, $"{stats.GpuUsage:0}%", "100%");
        Slot(cpu, $"{stats.CpuUsage:0}%", "100%");

        temperatureGroup.Visibility = snap.GpuTemp.Visible ? Visibility.Visible : Visibility.Collapsed;
        Slot(temperature, snap.GpuTemp.Text, snap.GpuTemp.Capacity);
        ApplyTemperatureBrush(temperature, ref gpuSeverity, snap.GpuTemp.Severity);

        cpuTemperatureGroup.Visibility = snap.CpuTemp.Visible ? Visibility.Visible : Visibility.Collapsed;
        Slot(cpuTemperature, snap.CpuTemp.Text, snap.CpuTemp.Capacity);
        ApplyTemperatureBrush(cpuTemperature, ref cpuSeverity, snap.CpuTemp.Severity);

        weatherIcon.Content = weatherInfo;
        weather.Text = weatherInfo is null ? "" : $"{weatherInfo.Temperature:0}°C";
        weatherGroup.Visibility = weatherInfo is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
