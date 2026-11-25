using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MochiDock.Core;

namespace MochiDock.Controls;

/// <summary>
/// Standalone status badge paired with <see cref="MascotControl"/> in the compact dock.
/// When <see cref="IsOverlay"/> is true, it floats diagonally at Mochi's upper-right shoulder (or Neko's lower-right cheek)
/// with zero layout shift so neither the mascot nor neighbouring capsule items jump.
/// </summary>
public partial class MascotStatusBadge : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty MoodProperty =
        DependencyProperty.Register(
            nameof(Mood),
            typeof(MascotMood),
            typeof(MascotStatusBadge),
            new PropertyMetadata(MascotMood.Idle, OnMoodChanged));

    public MascotMood Mood
    {
        get => (MascotMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    /// <summary>Optional per-instance badge style override (used by the Settings live preview).</summary>
    public MascotBadgeStyle? BadgeStyleOverride { get; set; }

    /// <summary>Disables the pop animation on this control (used by Settings previews).</summary>
    public bool DisablePop { get; set; }

    /// <summary>When true, anchors diagonally outside the mascot's corner with negative margins so layout width stays fixed.</summary>
    public bool IsOverlay { get; set; }

    private MascotControl.MascotTheme _theme = MascotControl.CurrentTheme;
    private MascotKind _kind = MascotControl.CurrentKind;

    public MascotStatusBadge()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            MascotControl.BadgeStyleChanged += OnBadgeStyleChanged;
            Refresh();
        };
        Unloaded += (_, _) => MascotControl.BadgeStyleChanged -= OnBadgeStyleChanged;
        Refresh();
    }

    private static void OnMoodChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MascotStatusBadge badge) badge.Refresh();
    }

    private void OnBadgeStyleChanged() => Refresh();

    public void ApplyTheme(MascotControl.MascotTheme theme, MascotKind kind)
    {
        _theme = theme;
        _kind = kind;
        Refresh();
    }

    public void Refresh()
    {
        var style = BadgeStyleOverride ?? MascotControl.CurrentBadgeStyle;
        if (style == MascotBadgeStyle.Off || MascotBadge.For(Mood, _kind) is not { } b)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        bool isCat = _kind is MascotKind.Kuro or MascotKind.Shiro;
        if (IsOverlay)
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            if (isCat)
            {
                VerticalAlignment = VerticalAlignment.Bottom;
                Margin = new Thickness(0, 0, -10, -2);
            }
            else
            {
                VerticalAlignment = VerticalAlignment.Top;
                Margin = new Thickness(0, -3, -10, 0);
            }
        }

        string themeName = MascotControl.ThemeNameOf(_theme);
        var glyph = MascotControl.BadgeGlyphs.TryGetValue(b.Glyph, out var g) ? g : Geometry.Empty;
        var shades = MascotBadge.ShadePair(b.Fill);

        BadgeCharm.Visibility = Visibility.Collapsed;
        BadgeBubble.Visibility = Visibility.Collapsed;
        BadgeSticker.Visibility = Visibility.Collapsed;

        switch (style)
        {
            case MascotBadgeStyle.Bubble:
            {
                BadgeBubbleGlyph.Data = glyph;
                BadgeBubbleGlyphShadow.Data = glyph;
                BadgeBubbleGlyph.Fill = MascotControl.Ink(b.Fill);
                var border = MascotControl.Ink(ThemePolicy.Normalize(themeName) == ThemePolicy.Matcha
                    ? MascotBadge.DarkRing
                    : "#B8C0D0");
                BadgeBubbleBody.BorderBrush = border;
                BadgeBubbleTail.Stroke = border;
                BadgeBubbleTail.Data = MascotControl.RawFrozen(
                    IsOverlay
                        ? (isCat ? "M 3.0,3.2 L -0.6,0.2 L 6.4,2.0 Z" : "M 3.0,13.6 L -0.6,16.6 L 6.4,14.8 Z")
                        : "M 3.2,6.2 L -0.8,9.0 L 3.2,11.6 Z");
                BadgeBubble.Visibility = Visibility.Visible;
                break;
            }
            case MascotBadgeStyle.Sticker:
            {
                BadgeStickerBack.Data = glyph;
                BadgeStickerFill.Data = glyph;
                BadgeStickerSheen.Data = glyph;
                var ring = MascotControl.Ink(MascotBadge.RingFor(themeName, b.Fill));
                BadgeStickerBack.Fill = ring;
                BadgeStickerBack.Stroke = ring;
                StickerGradTop.Color = MascotControl.ParseHexColor(shades.Top);
                StickerGradMid.Color = MascotControl.ParseHexColor(shades.Mid);
                StickerGradBottom.Color = MascotControl.ParseHexColor(shades.Bottom);
                BadgeSticker.Visibility = Visibility.Visible;
                break;
            }
            default:
            {
                BadgeGlyph.Data = glyph;
                BadgeGlyphShadow.Data = glyph;
                BadgeGradTop.Color = MascotControl.ParseHexColor(shades.Top);
                BadgeGradMid.Color = MascotControl.ParseHexColor(shades.Mid);
                BadgeGradBottom.Color = MascotControl.ParseHexColor(shades.Bottom);
                BadgeBody.BorderBrush = MascotControl.Ink(MascotBadge.RingFor(themeName, b.Fill));
                BadgeGlyph.Fill = MascotControl.Ink(b.Ink);
                BadgeCharm.Visibility = Visibility.Visible;
                break;
            }
        }

        Visibility = Visibility.Visible;
        if (Mood is MascotMood.Alert or MascotMood.Excited && !DisablePop && SystemParameters.ClientAreaAnimation && IsLoaded && IsVisible)
        {
            PopBadge();
        }
    }

    private void PopBadge()
    {
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var pop = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            foreach (var (ms, value) in new[] { (0, 0.0), (150, 1.15), (250, 1.0), (420, 1.14), (560, 1.0) })
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new SineEase { EasingMode = EasingMode.EaseOut }));
            BadgeScale.BeginAnimation(property, pop);
        }
    }
}
