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

    private static string? _appliedTheme;

    /// <summary>Raised on the UI thread after the palette switched to another theme (not on every re-apply).</summary>
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

    public static bool IsHojichaTheme(string? theme) =>
        string.Equals(theme, "Hojicha", StringComparison.OrdinalIgnoreCase);

    private static SolidColorBrush FrozenBrush(MediaColor color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    /// <summary>Applies the current theme's visual-tree overrides to a lazily instantiated card.</summary>
    public static void ApplyToElement(FrameworkElement element)
    {
        if (_appliedTheme == null) return;
        bool isMatcha = IsMatchaTheme(_appliedTheme);
        var mascotTheme = Controls.MascotControl.ToMascotTheme(_appliedTheme);
        ApplyElementStyle(element, isMatcha);
        UpdateVisualTree(element, isMatcha, mascotTheme);
    }

    public static void ApplyTheme(Window window, string theme)
    {
        theme = MochiDock.Core.ThemePolicy.Normalize(theme);
        if (_appliedTheme == theme) return;
        bool isMatcha = IsMatchaTheme(theme);
        bool isHojicha = IsHojichaTheme(theme);
        var app = WpfApplication.Current;
        if (app == null) return;
        var mascotTheme = Controls.MascotControl.ToMascotTheme(theme);
        Controls.MascotControl.CurrentTheme = mascotTheme;   // a Mochi created later starts with the right body

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
            app.Resources["IslandHighlightSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(38, 154, 63, 85));
            app.Resources["IslandTabHoverBrush"] = FrozenBrush(MediaColor.FromArgb(24, 78, 107, 54));
            app.Resources["IslandStatusBrush"] = FrozenBrush(MediaColor.FromRgb(120, 82, 0));
            app.Resources["IslandAccentColor"] = MediaColor.FromRgb(51, 82, 38);
            app.Resources["IslandAccentSubtleBrush"] = FrozenBrush(MediaColor.FromRgb(207, 218, 188));
            app.Resources["IslandSaturdayBrush"] = FrozenBrush(MediaColor.FromRgb(38, 98, 140));
            // Text-safe accents (>= 4.5:1 on the Matcha cards) replacing dark-theme-only hardcoded hexes.
            app.Resources["IslandGreenBrush"] = FrozenBrush(MediaColor.FromRgb(23, 105, 43));
            app.Resources["IslandOrangeBrush"] = FrozenBrush(MediaColor.FromRgb(138, 80, 0));
            app.Resources["IslandRedBrush"] = FrozenBrush(MediaColor.FromRgb(179, 38, 30));
            app.Resources["IslandBlueBrush"] = FrozenBrush(MediaColor.FromRgb(26, 95, 180));
            app.Resources["IslandInfoBrush"] = FrozenBrush(MediaColor.FromRgb(31, 95, 153));
            app.Resources["IslandWhatsAppInkBrush"] = FrozenBrush(MediaColor.FromRgb(245, 240, 223));
            app.Resources["IslandWhatsAppBrush"] = FrozenBrush(MediaColor.FromRgb(24, 107, 56));
            app.Resources["IslandWhatsAppSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(34, 37, 160, 80));
            app.Resources["IslandWhatsAppBorderBrush"] = FrozenBrush(MediaColor.FromArgb(110, 24, 107, 56));
            app.Resources["IslandPositiveBrush"] = FrozenBrush(MediaColor.FromRgb(35, 100, 52));
            SetBrushes(app, "#7A4A9E", "IslandPurpleBrush");
            SetBrushes(app, "#24283322", "IslandChartGridBrush");
            SetBrushes(app, "#40283322", "IslandChartGridStrongBrush");
            SetBrushes(app, "#4752C4", "IslandDiscordBrush");
            SetBrushes(app, "#FFFFFF", "IslandDiscordInkBrush");
            SetBrushes(app, "#1A8F44", "IslandSpotifyBrush");
            app.Resources["IslandPositiveSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(32, 47, 125, 70));
            app.Resources["IslandNegativeBrush"] = FrozenBrush(MediaColor.FromRgb(156, 47, 68));
            app.Resources["IslandNegativeSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(32, 183, 72, 93));
            app.Resources["IslandWarningBrush"] = FrozenBrush(MediaColor.FromRgb(138, 80, 0));
            app.Resources["IslandWarningSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(32, 184, 107, 17));
            SetBrushes(app, "#B0B792", "IslandGaugeTrackBrush", "IslandTrackBrush");
            app.Resources["IslandPrayerBadgeBackgroundBrush"] = FrozenBrush(MediaColor.FromRgb(207, 218, 188));


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
            app.Resources["IslandGroundBrush"] = FrozenBrush(MediaColor.FromRgb(0, 0, 0));
            app.Resources["IslandBorderBrush"] = FrozenBrush(MediaColor.FromRgb(44, 44, 58));
            app.Resources["IslandSurfaceBrush"] = FrozenBrush(MediaColor.FromRgb(12, 12, 16));
            app.Resources["IslandSubCardBackgroundBrush"] = FrozenBrush(MediaColor.FromRgb(16, 16, 22));
            app.Resources["IslandSubCardBorderBrush"] = FrozenBrush(MediaColor.FromRgb(44, 44, 58));
            app.Resources["IslandChipBackgroundBrush"] = FrozenBrush(MediaColor.FromRgb(16, 16, 22));
            app.Resources["IslandChipActiveBrush"] = FrozenBrush(MediaColor.FromRgb(26, 26, 38));
            app.Resources["IslandChipBorderBrush"] = FrozenBrush(MediaColor.FromRgb(50, 50, 74));
            app.Resources["IslandIconButtonBackgroundBrush"] = FrozenBrush(MediaColor.FromRgb(30, 30, 38));
            app.Resources["IslandIconButtonHoverBrush"] = FrozenBrush(MediaColor.FromRgb(48, 48, 60));
            app.Resources["IslandActionButtonBackgroundBrush"] = FrozenBrush(MediaColor.FromRgb(32, 32, 42));
            app.Resources["IslandActionButtonHoverBrush"] = FrozenBrush(MediaColor.FromRgb(52, 52, 66));
            app.Resources["IslandActionButtonPressedBrush"] = FrozenBrush(MediaColor.FromRgb(68, 68, 88));
            app.Resources["IslandIconButtonPressedBrush"] = FrozenBrush(MediaColor.FromRgb(62, 62, 78));
            app.Resources["IslandControlBackgroundBrush"] = FrozenBrush(MediaColor.FromRgb(24, 24, 34));
            app.Resources["IslandControlBorderBrush"] = FrozenBrush(MediaColor.FromRgb(62, 62, 82));
            app.Resources["IslandScrollBarThumbBrush"] = FrozenBrush(MediaColor.FromRgb(74, 74, 94));
            app.Resources["IslandScrollBarThumbHoverBrush"] = FrozenBrush(MediaColor.FromRgb(120, 120, 148));
            app.Resources["IslandTabActiveBrush"] = FrozenBrush(MediaColor.FromRgb(36, 36, 48));
            app.Resources["IslandTabHoverBrush"] = FrozenBrush(MediaColor.FromArgb(24, 255, 255, 255));
            app.Resources["IslandInkBrush"] = FrozenBrush(MediaColor.FromRgb(245, 245, 245));
            app.Resources["IslandSecondaryInkBrush"] = FrozenBrush(MediaColor.FromRgb(158, 158, 158));
            app.Resources["IslandMutedInkBrush"] = FrozenBrush(MediaColor.FromRgb(128, 128, 128));
            SetBrushes(app, "#EBEBF0", "IslandAccentBrush", "IslandAccentFillBrush", "IslandVolumeBrush", "IslandPrayerBadgeStrokeBrush");
            SetBrushes(app, "#D4BC8E", "IslandHighlightBrush", "IslandQiblaBrush");
            app.Resources["IslandHighlightSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(38, 212, 188, 142));
            SetBrushes(app, "#D4D4DF", "IslandSelectionBorderBrush");
            SetBrushes(app, "#24FFFFFF", "IslandSelectionFillBrush");
            SetBrushes(app, "#FFFFFF", "IslandSliderThumbBrush");
            SetBrushes(app, "#121216", "IslandMediaGlyphBrush", "IslandAccentButtonInkBrush");
            SetBrushes(app, "#00000000", "IslandHeaderSelectionBorderBrush");
            app.Resources["IslandSelectionColor"] = MediaColor.FromRgb(212, 212, 223);
            app.Resources["IslandStatusBrush"] = FrozenBrush(MediaColor.FromRgb(255, 214, 10));
            app.Resources["IslandAccentColor"] = MediaColor.FromRgb(235, 235, 240);
            app.Resources["IslandAccentSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(36, 255, 255, 255));
            app.Resources["IslandSaturdayBrush"] = FrozenBrush(MediaColor.FromRgb(56, 189, 248));
            app.Resources["IslandGreenBrush"] = FrozenBrush(MediaColor.FromRgb(48, 209, 88));
            app.Resources["IslandOrangeBrush"] = FrozenBrush(MediaColor.FromRgb(255, 159, 10));
            app.Resources["IslandRedBrush"] = FrozenBrush(MediaColor.FromRgb(255, 69, 58));
            app.Resources["IslandBlueBrush"] = FrozenBrush(MediaColor.FromRgb(10, 132, 255));
            app.Resources["IslandInfoBrush"] = FrozenBrush(MediaColor.FromRgb(100, 181, 246));
            app.Resources["IslandWhatsAppInkBrush"] = FrozenBrush(MediaColor.FromRgb(6, 43, 20));
            app.Resources["IslandWhatsAppBrush"] = FrozenBrush(MediaColor.FromRgb(37, 211, 102));
            app.Resources["IslandWhatsAppSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(26, 37, 211, 102));
            app.Resources["IslandWhatsAppBorderBrush"] = FrozenBrush(MediaColor.FromArgb(51, 37, 211, 102));
            app.Resources["IslandPositiveBrush"] = FrozenBrush(MediaColor.FromRgb(52, 211, 153));
            SetBrushes(app, "#BF5AF2", "IslandPurpleBrush");
            SetBrushes(app, "#14FFFFFF", "IslandChartGridBrush");
            SetBrushes(app, "#25FFFFFF", "IslandChartGridStrongBrush");
            SetBrushes(app, "#5865F2", "IslandDiscordBrush");
            SetBrushes(app, "#FFFFFF", "IslandDiscordInkBrush");
            SetBrushes(app, "#1DB954", "IslandSpotifyBrush");
            app.Resources["IslandPositiveSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(38, 52, 211, 153));
            app.Resources["IslandNegativeBrush"] = FrozenBrush(MediaColor.FromRgb(251, 113, 133));
            app.Resources["IslandNegativeSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(38, 251, 113, 133));
            app.Resources["IslandWarningBrush"] = FrozenBrush(MediaColor.FromRgb(245, 158, 11));
            app.Resources["IslandWarningSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(38, 245, 158, 11));
            app.Resources["IslandGaugeTrackBrush"] = FrozenBrush(MediaColor.FromRgb(44, 44, 64));
            app.Resources["IslandTrackBrush"] = FrozenBrush(MediaColor.FromRgb(44, 44, 64));
            app.Resources["IslandPrayerBadgeBackgroundBrush"] = FrozenBrush(MediaColor.FromRgb(34, 34, 44));
            SetBrushes(app, "#333336", "IslandVolumeTrackBrush");

            // Dynamic Media Lyric Brushes (Dark: Crisp White / Gray)
            app.Resources["ActiveLyricBrush"] = FrozenBrush(MediaColor.FromRgb(255, 255, 255));
            app.Resources["ActiveLyricRomajiBrush"] = FrozenBrush(MediaColor.FromArgb(208, 255, 255, 255));
            app.Resources["InactiveLyricBrush"] = FrozenBrush(MediaColor.FromArgb(140, 255, 255, 255));
            app.Resources["InactiveLyricRomajiBrush"] = FrozenBrush(MediaColor.FromArgb(110, 255, 255, 255));

            // Dynamic Weather Brushes
            app.Resources["WeatherCloudBrush"] = FrozenBrush(MediaColor.FromRgb(176, 176, 196));
            app.Resources["WeatherCloudStroke"] = FrozenBrush(MediaColor.FromArgb(0, 0, 0, 0));
            SetBrushes(app,"#FFFFFF","WeatherClearCloudBrush");
            SetBrushes(app,"#E0E0EA","WeatherPartlyCloudBrush");
            SetBrushes(app,"#FFAE00","WeatherSunBrush");
            SetBrushes(app,"#FFB800","WeatherPartlySunBrush");
            SetBrushes(app,"#FFD043","WeatherCoronaBrush");
            SetBrushes(app,"#9898AA","WeatherMistBrush");
            SetBrushes(app,"#E0F2FE","WeatherSnowflakeBrush");

            // Hojicha is the Default dark palette with warm roasted-tea surfaces and a caramel accent on top.
            if (isHojicha) ApplyHojichaPalette(app);
        }

        // 2. Recursively style / restore elements across the visual tree (Hojicha keeps the XAML's own dark colours)
        if (window != null)
        {
            UpdateVisualTree(window, isMatcha, mascotTheme);
        }

        if (_appliedTheme != theme)
        {
            _appliedTheme = theme;
            ThemeChanged?.Invoke();
        }
    }

    /// <summary>
    /// Hojicha: roasted-tea browns with a caramel accent, layered over the Default palette (brand colours such as WhatsApp, Discord and
    /// Spotify stay as they are). Every text brush is at least 4.5:1 on the sub-card (#1E1814); verify-theme-contrast checks it.
    /// </summary>
    private static void ApplyHojichaPalette(WpfApplication app)
    {
        SetBrushes(app, "#14100D", "IslandGroundBrush");
        SetBrushes(app, "#1A1511", "IslandSurfaceBrush");
        SetBrushes(app, "#1E1814", "IslandSubCardBackgroundBrush");
        SetBrushes(app, "#241C17", "IslandChipBackgroundBrush");
        SetBrushes(app, "#2E241D", "IslandChipActiveBrush");
        SetBrushes(app, "#30261F", "IslandTabActiveBrush");
        SetBrushes(app, "#14F3E9DC", "IslandTabHoverBrush");
        SetBrushes(app, "#221B16", "IslandControlBackgroundBrush");
        SetBrushes(app, "#2A211B", "IslandIconButtonBackgroundBrush", "IslandPrayerBadgeBackgroundBrush", "IslandEmailBadgeBackgroundBrush");
        SetBrushes(app, "#3A2E25", "IslandIconButtonHoverBrush");
        SetBrushes(app, "#4A3B30", "IslandIconButtonPressedBrush", "IslandControlBorderBrush");
        SetBrushes(app, "#2C231C", "IslandActionButtonBackgroundBrush");
        SetBrushes(app, "#3C3027", "IslandActionButtonHoverBrush");
        SetBrushes(app, "#4C3D31", "IslandActionButtonPressedBrush");
        SetBrushes(app, "#3A2E25", "IslandBorderBrush", "IslandSubCardBorderBrush", "IslandGaugeTrackBrush", "IslandTrackBrush", "IslandVolumeTrackBrush");
        SetBrushes(app, "#4A3A2E", "IslandChipBorderBrush");
        SetBrushes(app, "#5A4A3C", "IslandScrollBarThumbBrush");
        SetBrushes(app, "#8A7560", "IslandScrollBarThumbHoverBrush");
        SetBrushes(app, "#F3E9DC", "IslandInkBrush");
        SetBrushes(app, "#B8A794", "IslandSecondaryInkBrush");
        SetBrushes(app, "#9A8875", "IslandMutedInkBrush");
        SetBrushes(app, "#E8DCCB", "IslandEmailBadgeInkBrush");
        SetBrushes(app, "#E0A96D", "IslandAccentBrush", "IslandAccentFillBrush", "IslandVolumeBrush", "IslandPrayerBadgeStrokeBrush", "IslandSelectionBorderBrush");
        SetBrushes(app, "#2A1A0C", "IslandAccentButtonInkBrush", "IslandMediaGlyphBrush");
        SetBrushes(app, "#FFF3E4", "IslandSliderThumbBrush");
        app.Resources["IslandAccentColor"] = MediaColor.FromRgb(224, 169, 109);
        app.Resources["IslandSelectionColor"] = MediaColor.FromRgb(224, 169, 109);
        SetBrushes(app, "#26E0A96D", "IslandSelectionFillBrush");
        SetBrushes(app, "#2EE0A96D", "IslandAccentSubtleBrush");
        SetBrushes(app, "#E8C38A", "IslandHighlightBrush", "IslandQiblaBrush");
        SetBrushes(app, "#26E8C38A", "IslandHighlightSubtleBrush");
        SetBrushes(app, "#F2C14E", "IslandStatusBrush");
        SetBrushes(app, "#7CC4E8", "IslandSaturdayBrush");
        SetBrushes(app, "#8FD18B", "IslandGreenBrush");
        SetBrushes(app, "#F0A04B", "IslandOrangeBrush");
        SetBrushes(app, "#FF7A6B", "IslandRedBrush");
        SetBrushes(app, "#6FA8FF", "IslandBlueBrush");
        SetBrushes(app, "#8EC1F0", "IslandInfoBrush");
        SetBrushes(app, "#C9A0F0", "IslandPurpleBrush");
        SetBrushes(app, "#6CD4A0", "IslandPositiveBrush");
        SetBrushes(app, "#266CD4A0", "IslandPositiveSubtleBrush");
        SetBrushes(app, "#FF8FA0", "IslandNegativeBrush");
        SetBrushes(app, "#26FF8FA0", "IslandNegativeSubtleBrush");
        SetBrushes(app, "#F2B13C", "IslandWarningBrush");
        SetBrushes(app, "#26F2B13C", "IslandWarningSubtleBrush");
        SetBrushes(app, "#16F3E9DC", "IslandChartGridBrush");
        SetBrushes(app, "#28F3E9DC", "IslandChartGridStrongBrush");

        SetBrushes(app, "#FFF8EF", "ActiveLyricBrush");
        SetBrushes(app, "#D0FFF8EF", "ActiveLyricRomajiBrush");
        SetBrushes(app, "#8CF3E9DC", "InactiveLyricBrush");
        SetBrushes(app, "#6EF3E9DC", "InactiveLyricRomajiBrush");

        SetBrushes(app, "#BFAE98", "WeatherCloudBrush");
        SetBrushes(app, "#FFF6EA", "WeatherClearCloudBrush");
        SetBrushes(app, "#E6D9C6", "WeatherPartlyCloudBrush");
        SetBrushes(app, "#9A8875", "WeatherMistBrush");
    }

    private static void SetBrushes(WpfApplication app, string color, params string[] keys)
    {
        foreach (var key in keys) app.Resources[key] = FrozenBrush((MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private static void UpdateVisualTree(DependencyObject parent, bool isMatcha, Controls.MascotControl.MascotTheme mascotTheme)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Controls.MascotControl mascot)
            {
                mascot.ApplyTheme(mascotTheme);
                continue; // Never step into MascotControl children so eyes stay #1A1412!
            }
            if (child.GetType().Name.Contains("MascotControl"))
            {
                continue;
            }

            ApplyElementStyle(child, isMatcha);
            UpdateVisualTree(child, isMatcha, mascotTheme);
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
                        tb.Foreground = FrozenBrush(MediaColor.FromRgb(40,51,34));
                    }
                    else if (scb.Color.R > 130 && scb.Color.G > 130 && scb.Color.B > 130)
                    {
                        tb.Foreground = FrozenBrush(MediaColor.FromRgb(83,96,74));
                    }
                    else if (scb.Color.R > 90 && scb.Color.G > 90 && scb.Color.B > 90)
                    {
                        tb.Foreground = FrozenBrush(MediaColor.FromRgb(83,96,74));
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
                        border.Background = FrozenBrush(MediaColor.FromRgb(221,224,196));
                    }
                    else if (scbBg.Color.R < 60 && scbBg.Color.G < 60 && scbBg.Color.B < 75)
                    {
                        border.Background = FrozenBrush(MediaColor.FromRgb(241,215,181));
                    }
                }

                if (_originalBorderBrushes.TryGetValue(border, out var origBorder) && origBorder is SolidColorBrush scbBorder && scbBorder.Color.A > 0)
                {
                    if (scbBorder.Color.R < 70 && scbBorder.Color.G < 70 && scbBorder.Color.B < 90)
                    {
                        border.BorderBrush = FrozenBrush(MediaColor.FromRgb(165,175,137));
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
                        shape.Fill = FrozenBrush(MediaColor.FromArgb(scbFill.Color.A, 40, 51, 34));
                    }
                    else if (scbFill.Color.R < 40 && scbFill.Color.G < 40 && scbFill.Color.B < 50)
                    {
                        shape.Fill = FrozenBrush(MediaColor.FromArgb(scbFill.Color.A, 195, 201, 166));
                    }
                }

                if (_originalStrokes.TryGetValue(shape, out var origStroke) && origStroke is SolidColorBrush scbStroke)
                {
                    if (scbStroke.Color.R > 200 && scbStroke.Color.G > 200 && scbStroke.Color.B > 200)
                    {
                        shape.Stroke = FrozenBrush(MediaColor.FromArgb(scbStroke.Color.A, 40, 51, 34));
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
