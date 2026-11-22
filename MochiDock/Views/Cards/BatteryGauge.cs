using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MediaColor = System.Windows.Media.Color;
using Application = System.Windows.Application;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace MochiDock.Views.Cards;

/// <summary>
/// Drawn battery indicator: outline body, terminal nub and a level fill that fades from green through amber to red as the charge drops.
/// While charging the lightning bolt glows and the fill pulses softly. With <see cref="IsPsu"/> it draws a wall plug
/// instead, for a desktop PC with no percentage. Colours come from theme brushes so both palettes work.
/// </summary>
public sealed class BatteryGauge : Grid
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(nameof(Percent), typeof(int?), typeof(BatteryGauge), new PropertyMetadata(null, (d, _) => ((BatteryGauge)d).Update()));
    public static readonly DependencyProperty ChargingProperty = DependencyProperty.Register(nameof(Charging), typeof(bool), typeof(BatteryGauge), new PropertyMetadata(false, (d, _) => ((BatteryGauge)d).Update()));
    public static readonly DependencyProperty IsPsuProperty = DependencyProperty.Register(nameof(IsPsu), typeof(bool), typeof(BatteryGauge), new PropertyMetadata(false, (d, _) => ((BatteryGauge)d).Update()));

    public int? Percent { get => (int?)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }
    public bool IsPsu { get => (bool)GetValue(IsPsuProperty); set => SetValue(IsPsuProperty, value); }

    readonly Border body = new() { BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(6), Padding = new Thickness(2) };
    readonly Border nub = new() { Width = 3, Height = 10, CornerRadius = new CornerRadius(0, 2, 2, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly Border fill = new() { CornerRadius = new CornerRadius(4), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
    readonly Grid inner = new();
    readonly TextBlock bolt = new() { FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Text = "\uE945", HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly DropShadowEffect glow = new() { BlurRadius = 10, ShadowDepth = 0, Opacity = 0 };
    readonly Canvas psu = new() { Width = 56, Height = 28, Visibility = Visibility.Collapsed };

    public BatteryGauge()
    {
        Width = 56; Height = 28;
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.SetResourceReference(Border.BorderBrushProperty, "IslandMutedInkBrush");
        nub.SetResourceReference(Border.BackgroundProperty, "IslandMutedInkBrush");
        bolt.FontSize = 13;
        bolt.Effect = glow;
        inner.Children.Add(fill);
        inner.Children.Add(bolt);
        body.Child = inner;
        Children.Add(body);
        SetColumn(nub, 1);
        Children.Add(nub);
        BuildPsu();
        SetColumnSpan(psu, 2);
        Children.Add(psu);
        inner.SizeChanged += (_, _) => Update();
        Loaded += (_, _) => Update();
        IsVisibleChanged += (_, _) => { if (IsVisible) Update(); else StopAnimations(); };
        // Nothing keeps running on a page that is no longer shown.
        Unloaded += (_, _) => StopAnimations();
        Update();
    }

    /// <summary>Wall-plug drawing: two prongs, a rounded plug body and a cable, all in the accent colour (desktop PC on mains power).</summary>
    void BuildPsu()
    {
        foreach (var y in new[] { 10.0, 18.0 })
        {
            var prong = new Rectangle { Width = 9, Height = 3, RadiusX = 1.5, RadiusY = 1.5 };
            prong.SetResourceReference(Shape.FillProperty, "IslandAccentBrush");
            Canvas.SetLeft(prong, 8); Canvas.SetTop(prong, y - 1.5);
            psu.Children.Add(prong);
        }

        var plugBody = new Rectangle { Width = 16, Height = 20, RadiusX = 5, RadiusY = 5 };
        plugBody.SetResourceReference(Shape.FillProperty, "IslandAccentBrush");
        Canvas.SetLeft(plugBody, 16); Canvas.SetTop(plugBody, 4);
        psu.Children.Add(plugBody);

        var cable = new Path { StrokeThickness = 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Data = Geometry.Parse("M 32,14 C 40,14 42,8 48,8") };
        cable.SetResourceReference(Shape.StrokeProperty, "IslandAccentBrush");
        psu.Children.Add(cable);
    }

    void Update()
    {
        nub.Visibility = IsPsu ? Visibility.Collapsed : Visibility.Visible;
        body.Visibility = IsPsu ? Visibility.Collapsed : Visibility.Visible;
        psu.Visibility = IsPsu ? Visibility.Visible : Visibility.Collapsed;
        if (IsPsu)
        {
            fill.Visibility = Visibility.Collapsed;
            bolt.Visibility = Visibility.Collapsed;
            StopChargeAnimation();
            return;
        }
        var level = Percent is { } p ? Math.Clamp(p, 0, 100) : 0;
        var color = LevelColor(level, Theme("IslandGreenBrush", Colors.LimeGreen), Theme("IslandWarningBrush", Colors.Orange), Theme("IslandRedBrush", Colors.Red));
        fill.Visibility = Percent is null ? Visibility.Collapsed : Visibility.Visible;
        fill.Width = Math.Max(0, inner.ActualWidth * level / 100.0);
        fill.Background = new SolidColorBrush(color);
        var charging = Charging && Percent is not null;
        bolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
        bolt.SetResourceReference(TextBlock.ForegroundProperty, "IslandInkBrush");
        glow.Color = color;
        if (charging && IsVisible) StartChargeAnimation(); else StopChargeAnimation();
    }

    /// <summary>
    /// Level colour: full green down to half, then a gradual fade through the theme's amber to the theme's red by 5 %.
    /// A smooth ramp instead of three steps, so the colour already hints at how much is left.
    /// </summary>
    internal static MediaColor LevelColor(int percent, MediaColor green, MediaColor warn, MediaColor red)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (percent >= 50) return green;
        if (percent >= 20) return Lerp(warn, green, (percent - 20) / 30.0);
        if (percent > 5) return Lerp(red, warn, (percent - 5) / 15.0);
        return red;
    }

    static MediaColor Lerp(MediaColor from, MediaColor to, double t) => MediaColor.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * t), (byte)Math.Round(from.G + (to.G - from.G) * t), (byte)Math.Round(from.B + (to.B - from.B) * t));

    static MediaColor Theme(string key, MediaColor fallback) =>
        Application.Current?.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;

    // Soft pulse on the glow and the fill. Skipped when Windows animations are off (a static glow stays).
    void StartChargeAnimation()
    {
        if (!IsLoaded || !IsVisible || !SystemParameters.ClientAreaAnimation) { glow.Opacity = 0.9; return; }
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        fill.BeginAnimation(OpacityProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
    }

    void StopChargeAnimation()
    {
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        fill.BeginAnimation(OpacityProperty, null);
        glow.Opacity = 0;
        fill.Opacity = 1;
    }

    void StopAnimations()
    {
        StopChargeAnimation();
    }
}
