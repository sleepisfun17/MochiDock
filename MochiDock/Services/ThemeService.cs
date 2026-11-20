using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;
using WpfApplication = System.Windows.Application;

namespace MochiDock.Services;

public static class ThemeService
{
    private static readonly ConditionalWeakTable<DependencyObject, MediaBrush> _originalFills = new();
    private static readonly ConditionalWeakTable<DependencyObject, MediaBrush> _originalStrokes = new();
    private static readonly ConditionalWeakTable<DependencyObject, MediaBrush> _originalForegrounds = new();
    private static readonly ConditionalWeakTable<DependencyObject, MediaBrush> _originalBackgrounds = new();
    private static readonly ConditionalWeakTable<DependencyObject, MediaBrush> _originalBorderBrushes = new();

    private static bool? _appliedMatcha;

    /// <summary>Raised on the UI thread after the palette switched between Default and Matcha (not on every re-apply).</summary>
    public static event Action? ThemeChanged;

    /// <summary>
    /// For views that build parts of themselves in code and copy theme brushes while doing so (charts, generated rows): runs
    /// <paramref name="redraw"/> when the theme changes, right away if the view is on screen, otherwise the next time it is shown.
    /// Without this such a view keeps the previous theme's colours until its own data refresh redraws it.
    /// </summary>
    public static void RedrawOnThemeChange(FrameworkElement view, Action redraw)
    {
        bool stale = false;
        void Changed() { if (view.IsVisible) redraw(); else stale = true; }
        view.Loaded += (_, _) => { ThemeChanged -= Changed; ThemeChanged += Changed; };
        view.Unloaded += (_, _) => ThemeChanged -= Changed;
        view.IsVisibleChanged += (_, _) => { if (stale && view.IsVisible) { stale = false; redraw(); } };
    }

    public static bool IsMatchaTheme(string? theme) =>
        string.Equals(theme, "Matcha", StringComparison.OrdinalIgnoreCase);

    public static void ApplyTheme(Window window, string theme)
    {
        bool isMatcha = IsMatchaTheme(theme);
        var app = WpfApplication.Current;
        if (app == null) return;
        Controls.MascotControl.CurrentIsMatcha = isMatcha;   // a Mochi created later starts with the right body

        var uiFont = (System.Windows.Media.FontFamily)app.FindResource("QuicksandFont");
        if (!ReferenceEquals(app.Resources["IslandUiFont"], uiFont)) app.Resources["IslandUiFont"] = uiFont;

        app.Resources["IslandPrimaryReadingWeight"] = FontWeights.Bold;
        app.Resources["IslandHeadingWeight"] = FontWeights.Bold;
        app.Resources["IslandSmallLabelWeight"] = FontWeights.SemiBold;
        app.Resources["IslandSmallStrongWeight"] = FontWeights.SemiBold;
        app.Resources["IslandBodyWeight"] = FontWeights.SemiBold;

        // 1. Set / Update Dynamic Resource Brushes in Application.Current.Resources
        if (isMatcha)
        {
            // Warm oat, sage surfaces and matcha selections; dark green matcha is the accent.
            SetBrushes(app, "#EFE6CF", "IslandGroundBrush");
            SetBrushes(app, "#DDE0C4", "IslandSurfaceBrush", "IslandSubCardBackgroundBrush", "IslandChipBackgroundBrush",
                "IslandIconButtonBackgroundBrush", "IslandActionButtonBackgroundBrush", "IslandControlBackgroundBrush");
            SetBrushes(app, "#CFDABC", "IslandChipActiveBrush", "IslandTabActiveBrush", "IslandIconButtonHoverBrush", "IslandActionButtonHoverBrush", "IslandSelectionFillBrush", "IslandEmailBadgeBackgroundBrush", "IslandVolumeTrackBrush");
            SetBrushes(app, "#4E6B36", "IslandSelectionBorderBrush", "IslandHeaderSelectionBorderBrush");
            SetBrushes(app, "#BCCBA4", "IslandActionButtonPressedBrush", "IslandIconButtonPressedBrush");
            SetBrushes(app, "#EFE6CF", "IslandSliderThumbBrush");
            SetBrushes(app, "#F5F0DF", "IslandMediaGlyphBrush", "IslandAccentButtonInkBrush");
            app.Resources["IslandSelectionColor"] = MediaColor.FromRgb(78, 107, 54);
            SetBrushes(app, "#8E9873", "IslandBorderBrush", "IslandSubCardBorderBrush", "IslandChipBorderBrush", "IslandControlBorderBrush", "IslandScrollBarThumbBrush");
            SetBrushes(app, "#53604A", "IslandSecondaryInkBrush", "IslandMutedInkBrush", "IslandScrollBarThumbHoverBrush");
            SetBrushes(app, "#283322", "IslandInkBrush", "IslandEmailBadgeInkBrush", "ActiveLyricBrush", "ActiveLyricRomajiBrush", "InactiveLyricBrush", "InactiveLyricRomajiBrush");
            SetBrushes(app, "#335226", "IslandAccentBrush", "IslandAccentFillBrush", "IslandVolumeBrush", "IslandPrayerBadgeStrokeBrush");
            SetBrushes(app, "#9A3F55", "IslandHighlightBrush", "IslandQiblaBrush");
            app.Resources["IslandHighlightSubtleBrush"] = new SolidColorBrush(MediaColor.FromArgb(38, 154, 63, 85));
            app.Resources["IslandTabHoverBrush"] = new SolidColorBrush(MediaColor.FromArgb(24, 78, 107, 54));
            app.Resources["IslandStatusBrush"] = new SolidColorBrush(MediaColor.FromRgb(120, 82, 0));
            app.Resources["IslandAccentColor"] = MediaColor.FromRgb(51, 82, 38);
            app.Resources["IslandAccentSubtleBrush"] = new SolidColorBrush(MediaColor.FromRgb(207, 218, 188));
            app.Resources["IslandSaturdayBrush"] = new SolidColorBrush(MediaColor.FromRgb(38, 98, 140));
            // Text-safe accents (>= 4.5:1 on the Matcha cards) replacing dark-theme-only hardcoded hexes.
            app.Resources["IslandGreenBrush"] = new SolidColorBrush(MediaColor.FromRgb(23, 105, 43));
            app.Resources["IslandOrangeBrush"] = new SolidColorBrush(MediaColor.FromRgb(138, 80, 0));
            app.Resources["IslandRedBrush"] = new SolidColorBrush(MediaColor.FromRgb(179, 38, 30));
            app.Resources["IslandBlueBrush"] = new SolidColorBrush(MediaColor.FromRgb(26, 95, 180));
            app.Resources["IslandInfoBrush"] = new SolidColorBrush(MediaColor.FromRgb(31, 95, 153));
            app.Resources["IslandWhatsAppInkBrush"] = new SolidColorBrush(MediaColor.FromRgb(245, 240, 223));
            app.Resources["IslandWhatsAppBrush"] = new SolidColorBrush(MediaColor.FromRgb(24, 107, 56));
            app.Resources["IslandWhatsAppSubtleBrush"] = new SolidColorBrush(MediaColor.FromArgb(34, 37, 160, 80));
            app.Resources["IslandWhatsAppBorderBrush"] = new SolidColorBrush(MediaColor.FromArgb(110, 24, 107, 56));
            app.Resources["IslandPositiveBrush"] = new SolidColorBrush(MediaColor.FromRgb(35, 100, 52));
            SetBrushes(app, "#7A4A9E", "IslandPurpleBrush");
            SetBrushes(app, "#24283322", "IslandChartGridBrush");
            SetBrushes(app, "#40283322", "IslandChartGridStrongBrush");
            SetBrushes(app, "#4752C4", "IslandDiscordBrush");
            SetBrushes(app, "#FFFFFF", "IslandDiscordInkBrush");
            SetBrushes(app, "#1A8F44", "IslandSpotifyBrush");
            app.Resources["IslandPositiveSubtleBrush"] = new SolidColorBrush(MediaColor.FromArgb(32, 47, 125, 70));
            app.Resources["IslandNegativeBrush"] = new SolidColorBrush(MediaColor.FromRgb(156, 47, 68));
            app.Resources["IslandNegativeSubtleBrush"] = new SolidColorBrush(MediaColor.FromArgb(32, 183, 72, 93));
            app.Resources["IslandWarningBrush"] = new SolidColorBrush(MediaColor.FromRgb(138, 80, 0));
            app.Resources["IslandWarningSubtleBrush"] = new SolidColorBrush(MediaColor.FromArgb(32, 184, 107, 17));
            SetBrushes(app, "#B0B792", "IslandGaugeTrackBrush", "IslandTrackBrush");
            app.Resources["IslandPrayerBadgeBackgroundBrush"] = new SolidColorBrush(MediaColor.FromRgb(207, 218, 188));


            // Dynamic Media Lyric Brushes (Matcha: Espresso text on light foam ground)
            SetBrushes(app,"#53604A","InactiveLyricBrush","InactiveLyricRomajiBrush");

            // Dynamic Weather Brushes
            SetBrushes(app,"#F5F0DF","WeatherCloudBrush","WeatherClearCloudBrush","WeatherPartlyCloudBrush");
            SetBrushes(app,"#53604A","WeatherCloudStroke");
            SetBrushes(app,"#BD6B10","WeatherSunBrush","WeatherPartlySunBrush");
            SetBrushes(app,"#DB9236","WeatherCoronaBrush");
            SetBrushes(app,"#53604A","WeatherMistBrush","WeatherSnowflakeBrush");
        }
        else
        {
            // Default dark palette: pure black ground with silver and white accents
            SetBrushes(app, "#22222C", "IslandEmailBadgeBackgroundBrush");
            SetBrushes(app, "#E5E5EA", "IslandEmailBadgeInkBrush");
            app.Resources["IslandGroundBrush"] = new SolidColorBrush(MediaColor.FromRgb(0, 0, 0));
            app.Resources["IslandBorderBrush"] = new SolidColorBrush(MediaColor.FromRgb(44, 44, 58));
            app.Resources["IslandSurfaceBrush"] = new SolidColorBrush(MediaColor.FromRgb(12, 12, 16));
            app.Resources["IslandSubCardBackgroundBrush"] = new SolidColorBrush(MediaColor.FromRgb(16, 16, 22));
            app.Resources["IslandSubCardBorderBrush"] = new SolidColorBrush(MediaColor.FromRgb(44, 44, 58));
            app.Resources["IslandChipBackgroundBrush"] = new SolidColorBrush(MediaColor.FromRgb(16, 16, 22));
            app.Resources["IslandChipActiveBrush"] = new SolidColorBrush(MediaColor.FromRgb(26, 26, 38));
            app.Resources["IslandChipBorderBrush"] = new SolidColorBrush(MediaColor.FromRgb(50, 50, 74));
            app.Resources["IslandIconButtonBackgroundBrush"] = new SolidColorBrush(MediaColor.FromRgb(30, 30, 38));
            app.Resources["IslandIconButtonHoverBrush"] = new SolidColorBrush(MediaColor.FromRgb(48, 48, 60));
            app.Resources["IslandActionButtonBackgroundBrush"] = new SolidColorBrush(MediaColor.FromRgb(32, 32, 42));
            app.Resources["IslandActionButtonHoverBrush"] = new SolidColorBrush(MediaColor.FromRgb(52, 52, 66));
            app.Resources["IslandActionButtonPressedBrush"] = new SolidColorBrush(MediaColor.FromRgb(68, 68, 88));
            app.Resources["IslandIconButtonPressedBrush"] = new SolidColorBrush(MediaColor.FromRgb(62, 62, 78));
            app.Resources["IslandControlBackgroundBrush"] = new SolidColorBrush(MediaColor.FromRgb(24, 24, 34));
            app.Resources["IslandControlBorderBrush"] = new SolidColorBrush(MediaColor.FromRgb(62, 62, 82));
            app.Resources["IslandScrollBarThumbBrush"] = new SolidColorBrush(MediaColor.FromRgb(74, 74, 94));
            app.Resources["IslandScrollBarThumbHoverBrush"] = new SolidColorBrush(MediaColor.FromRgb(120, 120, 148));
            app.Resources["IslandTabActiveBrush"] = new SolidColorBrush(MediaColor.FromRgb(36, 36, 48));
            app.Resources["IslandTabHoverBrush"] = new SolidColorBrush(MediaColor.FromArgb(24, 255, 255, 255));
            app.Resources["IslandInkBrush"] = new SolidColorBrush(MediaColor.FromRgb(245, 245, 245));
            app.Resources["IslandSecondaryInkBrush"] = new SolidColorBrush(MediaColor.FromRgb(158, 158, 158));
            app.Resources["IslandMutedInkBrush"] = new SolidColorBrush(MediaColor.FromRgb(128, 128, 128));
            SetBrushes(app, "#EBEBF0", "IslandAccentBrush", "IslandAccentFillBrush", "IslandVolumeBrush", "IslandPrayerBadgeStrokeBrush");
            SetBrushes(app, "#D4BC8E", "IslandHighlightBrush", "IslandQiblaBrush");
            app.Resources["IslandHighlightSubtleBrush"] = new SolidColorBrush(MediaColor.FromArgb(38, 212, 188, 142));
            SetBrushes(app, "#D4D4DF", "IslandSelectionBorderBrush");
            SetBrushes(app, "#24FFFFFF", "IslandSelectionFillBrush");
            SetBrushes(app, "#FFFFFF", "IslandSliderThumbBrush");
            SetBrushes(app, "#121216", "IslandMediaGlyphBrush", "IslandAccentButtonInkBrush");
            SetBrushes(app, "#00000000", "IslandHeaderSelectionBorderBrush");
            app.Resources["IslandSelectionColor"] = MediaColor.FromRgb(212, 212, 223);
            app.Resources["IslandStatusBrush"] = new SolidColorBrush(MediaColor.FromRgb(255, 214, 10));
            app.Resources["IslandAccentColor"] = MediaColor.FromRgb(235, 235, 240);
            app.Resources["IslandAccentSubtleBrush"] = new SolidColorBrush(MediaColor.FromArgb(36, 255, 255, 255));
            app.Resources["IslandSaturdayBrush"] = new SolidColorBrush(MediaColor.FromRgb(56, 189, 248));
            app.Resources["IslandGreenBrush"] = new SolidColorBrush(MediaColor.FromRgb(48, 209, 88));
            app.Resources["IslandOrangeBrush"] = new SolidColorBrush(MediaColor.FromRgb(255, 159, 10));
            app.Resources["IslandRedBrush"] = new SolidColorBrush(MediaColor.FromRgb(255, 69, 58));
            app.Resources["IslandBlueBrush"] = new SolidColorBrush(MediaColor.FromRgb(10, 132, 255));
            app.Resources["IslandInfoBrush"] = new SolidColorBrush(MediaColor.FromRgb(100, 181, 246));
            app.Resources["IslandWhatsAppInkBrush"] = new SolidColorBrush(MediaColor.FromRgb(6, 43, 20));
            app.Resources["IslandWhatsAppBrush"] = new SolidColorBrush(MediaColor.FromRgb(37, 211, 102));
            app.Resources["IslandWhatsAppSubtleBrush"] = new SolidColorBrush(MediaColor.FromArgb(26, 37, 211, 102));
            app.Resources["IslandWhatsAppBorderBrush"] = new SolidColorBrush(MediaColor.FromArgb(51, 37, 211, 102));
            app.Resources["IslandPositiveBrush"] = new SolidColorBrush(MediaColor.FromRgb(52, 211, 153));
            SetBrushes(app, "#BF5AF2", "IslandPurpleBrush");
            SetBrushes(app, "#14FFFFFF", "IslandChartGridBrush");
            SetBrushes(app, "#25FFFFFF", "IslandChartGridStrongBrush");
            SetBrushes(app, "#5865F2", "IslandDiscordBrush");
            SetBrushes(app, "#FFFFFF", "IslandDiscordInkBrush");
            SetBrushes(app, "#1DB954", "IslandSpotifyBrush");
            app.Resources["IslandPositiveSubtleBrush"] = new SolidColorBrush(MediaColor.FromArgb(38, 52, 211, 153));
            app.Resources["IslandNegativeBrush"] = new SolidColorBrush(MediaColor.FromRgb(251, 113, 133));
            app.Resources["IslandNegativeSubtleBrush"] = new SolidColorBrush(MediaColor.FromArgb(38, 251, 113, 133));
            app.Resources["IslandWarningBrush"] = new SolidColorBrush(MediaColor.FromRgb(245, 158, 11));
            app.Resources["IslandWarningSubtleBrush"] = new SolidColorBrush(MediaColor.FromArgb(38, 245, 158, 11));
            app.Resources["IslandGaugeTrackBrush"] = new SolidColorBrush(MediaColor.FromRgb(44, 44, 64));
            app.Resources["IslandTrackBrush"] = new SolidColorBrush(MediaColor.FromRgb(44, 44, 64));
            app.Resources["IslandPrayerBadgeBackgroundBrush"] = new SolidColorBrush(MediaColor.FromRgb(34, 34, 44));
            SetBrushes(app, "#333336", "IslandVolumeTrackBrush");

            // Dynamic Media Lyric Brushes (Dark: Crisp White / Gray)
            app.Resources["ActiveLyricBrush"] = new SolidColorBrush(MediaColor.FromRgb(255, 255, 255));
            app.Resources["ActiveLyricRomajiBrush"] = new SolidColorBrush(MediaColor.FromArgb(208, 255, 255, 255));
            app.Resources["InactiveLyricBrush"] = new SolidColorBrush(MediaColor.FromArgb(140, 255, 255, 255));
            app.Resources["InactiveLyricRomajiBrush"] = new SolidColorBrush(MediaColor.FromArgb(110, 255, 255, 255));

            // Dynamic Weather Brushes
            app.Resources["WeatherCloudBrush"] = new SolidColorBrush(MediaColor.FromRgb(176, 176, 196));
            app.Resources["WeatherCloudStroke"] = new SolidColorBrush(MediaColor.FromArgb(0, 0, 0, 0));
            SetBrushes(app,"#FFFFFF","WeatherClearCloudBrush");
            SetBrushes(app,"#E0E0EA","WeatherPartlyCloudBrush");
            SetBrushes(app,"#FFAE00","WeatherSunBrush");
            SetBrushes(app,"#FFB800","WeatherPartlySunBrush");
            SetBrushes(app,"#FFD043","WeatherCoronaBrush");
            SetBrushes(app,"#9898AA","WeatherMistBrush");
            SetBrushes(app,"#E0F2FE","WeatherSnowflakeBrush");
        }

        // 2. Recursively style / restore elements across the visual tree
        if (window != null)
        {
            UpdateVisualTree(window, isMatcha);
        }

        if (_appliedMatcha != isMatcha)
        {
            _appliedMatcha = isMatcha;
            ThemeChanged?.Invoke();
        }
    }

    private static void SetBrushes(WpfApplication app, string color, params string[] keys)
    {
        foreach (var key in keys) app.Resources[key] = new SolidColorBrush((MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private static void UpdateVisualTree(DependencyObject parent, bool isMatcha)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Controls.MascotControl mascot)
            {
                mascot.ApplyTheme(isMatcha);
                continue; // Never step into MascotControl children so eyes stay #1A1412!
            }
            if (child.GetType().Name.Contains("MascotControl"))
            {
                continue;
            }

            ApplyElementStyle(child, isMatcha);
            UpdateVisualTree(child, isMatcha);
        }
    }

    private static bool IsInsideAccentFill(DependencyObject elem)
    {
        var app = WpfApplication.Current;
        var fillBrush = app?.Resources["IslandAccentFillBrush"] as MediaBrush;
        var parent = VisualTreeHelper.GetParent(elem);
        while (parent != null)
        {
            if (parent is System.Windows.Controls.Control ctrl)
            {
                if (ctrl.Background is SolidColorBrush scb && fillBrush is SolidColorBrush fsb && scb.Color == fsb.Color)
                    return true;
            }
            if (parent is Border b)
            {
                if (b.Background is SolidColorBrush scb && fillBrush is SolidColorBrush fsb && scb.Color == fsb.Color)
                    return true;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
        return false;
    }

    public static void ApplyElementStyle(DependencyObject elem, bool isMatcha)
    {
        // Don't recolor the Mascot
        if (elem.GetType().Name.Contains("MascotControl")) return;

        if (elem is TextBlock tb)
        {
            if (IsInsideAccentFill(tb))
            {
                tb.SetResourceReference(TextBlock.ForegroundProperty, "IslandAccentButtonInkBrush");
                return;
            }

            if (isMatcha)
            {
                // Only intercept explicit SolidColorBrush (leave {DynamicResource} and {Binding} alone)
                if (!_originalForegrounds.TryGetValue(tb, out _))
                {
                    if (tb.ReadLocalValue(TextBlock.ForegroundProperty) is SolidColorBrush localBrush)
                        _originalForegrounds.Add(tb, localBrush);
                }

                if (_originalForegrounds.TryGetValue(tb, out var cur) && cur is SolidColorBrush scb)
                {
                    // Map white / light grey to deep matcha ink, preserving alpha contrast
                    if (scb.Color.R > 200 && scb.Color.G > 200 && scb.Color.B > 200)
                    {
                        tb.Foreground = new SolidColorBrush(MediaColor.FromRgb(40,51,34));
                    }
                    else if (scb.Color.R > 130 && scb.Color.G > 130 && scb.Color.B > 130)
                    {
                        tb.Foreground = new SolidColorBrush(MediaColor.FromRgb(83,96,74));
                    }
                    else if (scb.Color.R > 90 && scb.Color.G > 90 && scb.Color.B > 90)
                    {
                        tb.Foreground = new SolidColorBrush(MediaColor.FromRgb(83,96,74));
                    }
                }
            }
            else
            {
                // In Dark mode, restore previously cached local foreground if any
                if (_originalForegrounds.TryGetValue(tb, out var orig))
                {
                    tb.Foreground = orig;
                }
            }
        }
        else if (elem is Border border)
        {
            if (isMatcha)
            {
                if (!_originalBackgrounds.TryGetValue(border, out _))
                {
                    if (border.ReadLocalValue(Border.BackgroundProperty) is SolidColorBrush localBg)
                        _originalBackgrounds.Add(border, localBg);
                }
                if (!_originalBorderBrushes.TryGetValue(border, out _))
                {
                    if (border.ReadLocalValue(Border.BorderBrushProperty) is SolidColorBrush localBorder)
                        _originalBorderBrushes.Add(border, localBorder);
                }

                if (_originalBackgrounds.TryGetValue(border, out var origBg) && origBg is SolidColorBrush scbBg && scbBg.Color.A > 0)
                {
                    // Map dark container boxes (#101016, #12121A, #14141C, #181822, #1C1C26) to Matcha card surface
                    if (scbBg.Color.R < 35 && scbBg.Color.G < 35 && scbBg.Color.B < 45)
                    {
                        border.Background = new SolidColorBrush(MediaColor.FromRgb(221,224,196));
                    }
                    else if (scbBg.Color.R < 60 && scbBg.Color.G < 60 && scbBg.Color.B < 75)
                    {
                        border.Background = new SolidColorBrush(MediaColor.FromRgb(241,215,181));
                    }
                }

                if (_originalBorderBrushes.TryGetValue(border, out var origBorder) && origBorder is SolidColorBrush scbBorder && scbBorder.Color.A > 0)
                {
                    if (scbBorder.Color.R < 70 && scbBorder.Color.G < 70 && scbBorder.Color.B < 90)
                    {
                        border.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(165,175,137));
                    }
                }
            }
            else
            {
                if (_originalBackgrounds.TryGetValue(border, out var origBg))
                {
                    border.Background = origBg;
                }

                if (_originalBorderBrushes.TryGetValue(border, out var origBorder))
                {
                    border.BorderBrush = origBorder;
                }
            }
        }
        else if (elem is Shape shape)
        {
            if (isMatcha)
            {
                if (!_originalFills.TryGetValue(shape, out _))
                {
                    if (shape.ReadLocalValue(Shape.FillProperty) is SolidColorBrush localFill)
                        _originalFills.Add(shape, localFill);
                }
                if (!_originalStrokes.TryGetValue(shape, out _))
                {
                    if (shape.ReadLocalValue(Shape.StrokeProperty) is SolidColorBrush localStroke)
                        _originalStrokes.Add(shape, localStroke);
                }

                if (_originalFills.TryGetValue(shape, out var origFill) && origFill is SolidColorBrush scbFill)
                {
                    if (scbFill.Color.R > 220 && scbFill.Color.G > 220 && scbFill.Color.B > 220)
                    {
                        shape.Fill = new SolidColorBrush(MediaColor.FromArgb(scbFill.Color.A, 40, 51, 34));
                    }
                    else if (scbFill.Color.R < 40 && scbFill.Color.G < 40 && scbFill.Color.B < 50)
                    {
                        shape.Fill = new SolidColorBrush(MediaColor.FromArgb(scbFill.Color.A, 195, 201, 166));
                    }
                }

                if (_originalStrokes.TryGetValue(shape, out var origStroke) && origStroke is SolidColorBrush scbStroke)
                {
                    if (scbStroke.Color.R > 200 && scbStroke.Color.G > 200 && scbStroke.Color.B > 200)
                    {
                        shape.Stroke = new SolidColorBrush(MediaColor.FromArgb(scbStroke.Color.A, 40, 51, 34));
                    }
                }
            }
            else
            {
                if (_originalFills.TryGetValue(shape, out var origFill))
                {
                    shape.Fill = origFill;
                }

                if (_originalStrokes.TryGetValue(shape, out var origStroke))
                {
                    shape.Stroke = origStroke;
                }
            }
        }
    }
}
