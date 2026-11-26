using System.Windows;
using System.Windows.Media.Animation;
using MochiDock.ViewModels;

namespace MochiDock;

public partial class MainWindow
{
    private (double width, double height) GetCardDimensions(IslandCardType cardType)
    {
        var (w, h) = cardType switch
        {
            IslandCardType.WhatsApp => (_viewModel.RecentWhatsAppMessages.Count > 1 ? (620, 340) : (620, 295)),
            IslandCardType.Discord => (620, 230),
            IslandCardType.Apps => (620, 230),
            IslandCardType.Settings => (620, 400),
            IslandCardType.Hardware => (620, 345),
            IslandCardType.Today => (620, 360),
            IslandCardType.FxRates => (620, 360),
            IslandCardType.AiUsage => (620, AiUsageCardHeight()),
            IslandCardType.Script => (620, 300),
            IslandCardType.Downloads => (620, 350),
            IslandCardType.Email => (620, EmailCardHeight()),
            IslandCardType.Media => (620, 170),
            IslandCardType.Summary => (620, _viewModel.HasSummaryLines ? 350 : 250),
            IslandCardType.Weather => (620, 275),
            IslandCardType.Pomodoro => (620, 170),
            IslandCardType.Prayer => (620, 270),
            IslandCardType.FileShelf => (620, 410),
            IslandCardType.Clipboard => (620, 250),
            _ => (620, 200)
        };
        h += BannerAllowance();
        return (w, h);
    }

    // Room for top banners above the card (welcome banner and/or update banner). Each follows its own visibility so cards never clip.
    private double BannerAllowance()
    {
        double extra = 0;
        if (FirstRunWelcomeBanner is { Visibility: Visibility.Visible } welcome)
        {
            double measured = welcome.ActualHeight > 0 ? welcome.ActualHeight + welcome.Margin.Top + welcome.Margin.Bottom : 0;
            extra += Math.Max(58, Math.Ceiling(measured));
        }
        if (UpdateBanner is { Visibility: Visibility.Visible } update)
        {
            double measured = update.ActualHeight > 0 ? update.ActualHeight + update.Margin.Top + update.Margin.Bottom : 0;
            extra += Math.Max(44, Math.Ceiling(measured));
        }
        return extra;
    }

    private void ResizeForBanners()
    {
        if (_windowClosed || !_viewModel.IsExpanded || ExpandedBorder.Visibility != Visibility.Visible) return;
        var (w, h) = GetCardDimensions(_viewModel.ActiveCardType);
        if (Math.Abs(ExpandedBorder.ActualHeight - h) > 0.5) AnimateExpandedDimensions(w, h);
    }

    private double MeasurePrayerArrivalWidth()
    {
        string title = _viewModel?.PrayerArrivalTitle ?? "";
        string subtitle = _viewModel?.PrayerArrivalSubtitle ?? "";
        var fontFamily = (System.Windows.Media.FontFamily)(TryFindResource("IslandUiFont") ?? CapsuleResolutionText.FontFamily);
        var titleWeight = (FontWeight)(TryFindResource("IslandPrimaryReadingWeight") ?? FontWeights.SemiBold);
        var subWeight = (FontWeight)(TryFindResource("IslandSmallLabelWeight") ?? FontWeights.Medium);
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (dpi <= 0) dpi = 1.0;

        var titleTypeface = new System.Windows.Media.Typeface(fontFamily, FontStyles.Normal, titleWeight, FontStretches.Normal);
        var titleFormatted = new System.Windows.Media.FormattedText(
            title,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            titleTypeface,
            13.0,
            System.Windows.Media.Brushes.Black,
            dpi);

        var subTypeface = new System.Windows.Media.Typeface(fontFamily, FontStyles.Normal, subWeight, FontStretches.Normal);
        var subFormatted = new System.Windows.Media.FormattedText(
            subtitle,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            subTypeface,
            11.0,
            System.Windows.Media.Brushes.Black,
            dpi);

        double textWidth = Math.Max(titleFormatted.WidthIncludingTrailingWhitespace, subFormatted.WidthIncludingTrailingWhitespace);
        // Moon badge (30 DIPs) + badge right margin (10 DIPs) = 40 DIPs
        // CapsulePrayerArrivalOverlay left margin (14 DIPs) + right corner-safe margin (24 DIPs) = 38 DIPs
        // Total chrome padding = 78 DIPs
        return Math.Max(180, Math.Ceiling(textWidth + 78));
    }

    private double GetCurrentCompactTargetHeight()
    {
        if (_viewModel == null) return 36.0;
        if (UseCompactGamingReadout()) return 32.0;
        if (_viewModel.IsPrayerArrivalVisible) return 48.0;
        if (_viewModel.IsStatsShownInCapsule) return 48.0;
        if (_viewModel.HasCompactLyricRomaji) return 48.0;
        if (_viewModel.IsNotificationInMainPill) return 48.0; // Notification capsule height = 48 DIPs
        return 36.0;
    }

    private double GetCurrentCompactTargetWidth()
    {
        if (_isAdjustingVolume) return 240.0;
        if (CapsuleResolutionOverlay.Visibility == Visibility.Visible) return _lastResolutionNoticeWidth;
        if (CapsulePowerOverlay.Visibility == Visibility.Visible && !_viewModel.IsPrayerArrivalVisible) return _lastPowerNoticeWidth;
        if (UseCompactGamingReadout())
        {
            GamingCapsuleContent.Measure(new System.Windows.Size(double.PositiveInfinity, 32));
            // The chrome border eats into the inner width, so add it: the content (with its own 12-DIP margins) then fits without clipping.
            return Math.Ceiling(GamingCapsuleContent.DesiredSize.Width + CompactPillBorder.BorderThickness.Left + CompactPillBorder.BorderThickness.Right);
        }
        if (_viewModel.IsPrayerArrivalVisible)
        {
            return MeasurePrayerArrivalWidth();
        }
        if (_viewModel.IsPrayerReminderVisible) return PrayerReminderContent.MeasureCapsuleWidth();
        if (_viewModel.IsHardwareNoticeVisible) return MeasureHardwareNoticeWidth();
        if (_viewModel.IsStatsShownInCapsule)
        {
            // Gaming Mode displays FPS + CPU + GPU + optional right-aligned weather
            bool hasWeather = _viewModel.IsWeatherInMainPill;
            if (_viewModel.CompactCapsuleIconType == "Gaming")
            {
                return hasWeather ? 460 : 380;
            }
            return hasWeather ? 435 : 360;
        }

        if (_viewModel.IsNotificationInMainPill)
        {
            // Scale notification capsule cleanly (280px to 340px)
            // Capped at 340px so it never overlaps the far-right close [X] button area (X ~ 480px)
            int textLen = _viewModel.CompactCapsuleText.Split('\n').Max(l => l.Length);
            int baseWidth = textLen <= 15 ? 280 : (textLen <= 30 ? 315 : 340);
            if (_viewModel.ShowRotationPosition) baseWidth += 22; // Room for 1/3 badge
            return Math.Min(350, baseWidth);
        }

        if (_viewModel.HasCompactLyricRomaji) return 380; // Two-line lyric with its romaji
        if (_viewModel.CurrentMedia.IsPlaying) return 340; // Generous width for song titles & smooth marquee
        if (MeasureClockWeatherLayout() is { } clockWeather) return clockWeather.PillWidth;
        if (_viewModel.IsWeatherInMainPill) return 195; // Pomodoro + weather: Mochi + clock + both badges
        if (_viewModel.IsPomodoroInMainPill) return 260; // Focus timer pill
        if (_viewModel.IsPrayerInMainPill) return 240; // Prayer countdown badge
        return 125; // Resting state: symmetrical pill showing [ Mascot  11:13 ]
    }

    private double MeasureHardwareNoticeWidth()
    {
        string text = _viewModel?.HardwareNoticeText ?? "";
        var fontFamily = (System.Windows.Media.FontFamily)(TryFindResource("IslandUiFont") ?? CapsuleResolutionText.FontFamily);
        var weight = (FontWeight)(TryFindResource("IslandBodyWeight") ?? FontWeights.SemiBold);
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (dpi <= 0) dpi = 1.0;

        var formatted = new System.Windows.Media.FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface(fontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            12.0,
            System.Windows.Media.Brushes.Black,
            dpi);

        return Math.Clamp(Math.Ceiling(formatted.WidthIncludingTrailingWhitespace + 96), 240, 440);
    }

    // The resting capsule with weather: [Mochi] clock [weather]. Either the clock is exactly centred with Mochi and the weather at the edges,
    // or the three items are evenly spaced (Settings > Appearance, CapsuleClockLayout). The widths are measured, so the clock is never
    // clipped and any temperature fits.
    private bool IsClockWeatherPill() =>
        _viewModel.IsWeatherInMainPill && !_viewModel.IsPomodoroInMainPill && !_viewModel.CurrentMedia.IsPlaying &&
        !_viewModel.HasCompactLyricRomaji && !_viewModel.IsStatsShownInCapsule && !_viewModel.IsHardwareNoticeVisible &&
        !_viewModel.IsPrayerReminderVisible && !_viewModel.IsPrayerArrivalVisible && !_viewModel.IsNotificationInMainPill &&
        !UseCompactGamingReadout() && !string.IsNullOrEmpty(_viewModel.CompactCapsuleText) && _viewModel.CompactCapsuleText == _viewModel.CurrentTimeText;

    private MochiDock.Core.CapsuleClockLayoutResult? MeasureClockWeatherLayout() =>
        _viewModel != null && IsClockWeatherPill()
            ? MochiDock.Controls.CapsuleClockWeatherArranger.Measure(CapsuleMarquee, _viewModel.CompactCapsuleText, PillMascotHost, PillWeatherBadge,
                CompactPillBorder.BorderThickness.Left + CompactPillBorder.BorderThickness.Right,
                MochiDock.Core.CapsuleClockLayout.Parse(_viewModel.Settings.CapsuleClockLayout))
            : null;

    private void ApplyClockWeatherLayout()
    {
        if (MeasureClockWeatherLayout() is { } layout)
            MochiDock.Controls.CapsuleClockWeatherArranger.Apply(layout, PillCol0, PillCol2, PillMascotHost, PillRightAnchors, PillWeatherBadge, PillTickerContent);
        else
            MochiDock.Controls.CapsuleClockWeatherArranger.Reset(PillCol0, PillCol2, PillMascotHost, PillRightAnchors, PillTickerContent);
    }

    private bool _gamingPillShown;
    private void UpdateCompactPillDimensions()
    {
        bool gaming = UseCompactGamingReadout();
        var s = _viewModel.Settings;
        CompactGamingReadout.Update(
            _viewModel.HardwareStats,
            _viewModel.IsWeatherInMainPill ? _viewModel.WeatherInfo : null,
            new MochiDock.Core.TemperatureThresholds(s.CpuTempWarmThreshold, s.CpuTempHotThreshold, s.GpuTempWarmThreshold, s.GpuTempHotThreshold));

        if (_isAdjustingVolume)
        {
            GamingCapsuleContent.Visibility = Visibility.Collapsed;
            PillNormalContent.Visibility = Visibility.Collapsed;
            CapsuleResolutionOverlay.Visibility = Visibility.Collapsed;
            CapsulePrayerArrivalOverlay.Visibility = Visibility.Collapsed;
            CapsulePowerOverlay.Visibility = Visibility.Collapsed;
            CapsuleVolumeOverlay.Visibility = Visibility.Visible;
        }
        else if (CapsuleResolutionOverlay.Visibility == Visibility.Visible)
        {
            GamingCapsuleContent.Visibility = Visibility.Collapsed;
            PillNormalContent.Visibility = Visibility.Collapsed;
            CapsuleVolumeOverlay.Visibility = Visibility.Collapsed;
            CapsulePrayerArrivalOverlay.Visibility = Visibility.Collapsed;
            CapsulePowerOverlay.Visibility = Visibility.Collapsed;
        }
        else if (_viewModel.IsPrayerArrivalVisible)
        {
            GamingCapsuleContent.Visibility = Visibility.Collapsed;
            PillNormalContent.Visibility = Visibility.Collapsed;
            CapsuleVolumeOverlay.Visibility = Visibility.Collapsed;
            CapsuleResolutionOverlay.Visibility = Visibility.Collapsed;
            CapsulePowerOverlay.Visibility = Visibility.Collapsed;
            CapsulePrayerArrivalOverlay.Visibility = Visibility.Visible;
        }
        else if (CapsulePowerOverlay.Visibility == Visibility.Visible)
        {
            // Charger / low-battery notice: yields to volume, the resolution toast and prayer arrival above.
            GamingCapsuleContent.Visibility = Visibility.Collapsed;
            PillNormalContent.Visibility = Visibility.Collapsed;
            CapsuleVolumeOverlay.Visibility = Visibility.Collapsed;
            CapsuleResolutionOverlay.Visibility = Visibility.Collapsed;
            CapsulePrayerArrivalOverlay.Visibility = Visibility.Collapsed;
        }
        else
        {
            CapsuleVolumeOverlay.Visibility = Visibility.Collapsed;
            CapsuleResolutionOverlay.Visibility = Visibility.Collapsed;
            CapsulePrayerArrivalOverlay.Visibility = Visibility.Collapsed;
            CapsulePowerOverlay.Visibility = Visibility.Collapsed;
            GamingCapsuleContent.Visibility = gaming ? Visibility.Visible : Visibility.Collapsed;
            PillNormalContent.Visibility = gaming ? Visibility.Collapsed : Visibility.Visible;
        }

        ApplyClockWeatherLayout();
        double targetWidth = GetCurrentCompactTargetWidth();
        double targetHeight = GetCurrentCompactTargetHeight();

        // Gaming readout: stay in the same layout between ticks, so follow the content immediately instead of restarting a 200 ms
        // animation (the content is centered, so a pill that lags behind it clips the Mochi on the left and the last value on the right).
        bool stayInGaming = gaming && _gamingPillShown && CompactPillBorder.ActualWidth > 0;
        if (!gaming) CompactGamingReadout.Reset();
        _gamingPillShown = gaming;
        if (stayInGaming)
        {
            if (Math.Abs(CompactPillBorder.ActualWidth - targetWidth) > 0.5)
            {
                CompactPillBorder.BeginAnimation(WidthProperty, null);
                CompactPillBorder.Width = targetWidth;
            }
            CompactPillBorder.BeginAnimation(HeightProperty, null);
            CompactPillBorder.Height = targetHeight;
            return;
        }

        double curWidth = CompactPillBorder.ActualWidth > 0 ? CompactPillBorder.ActualWidth : 44;
        double curHeight = CompactPillBorder.ActualHeight > 0 ? CompactPillBorder.ActualHeight : 36;
        bool widthChanged = Math.Abs(curWidth - targetWidth) >= 0.5;
        bool heightChanged = Math.Abs(curHeight - targetHeight) >= 0.5;
        if (!widthChanged && !heightChanged) return;

        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (widthChanged)
        {
            var wAnim = new DoubleAnimation(curWidth, targetWidth, duration)
            {
                EasingFunction = ease
            };
            wAnim.Completed += (s, e) =>
            {
                if (!_isAdjustingVolume)
                {
                    CapsuleMarquee.UpdateMarquee(resetScroll: false);
                }
            };
            CompactPillBorder.BeginAnimation(WidthProperty, wAnim);
        }

        if (heightChanged)
        {
            var hAnim = new DoubleAnimation(curHeight, targetHeight, duration)
            {
                EasingFunction = ease
            };
            CompactPillBorder.BeginAnimation(HeightProperty, hAnim);
        }
    }

    private bool _prayerSizePending;
    private void UpdateCompactPillWidth()
    {
        if (!_viewModel.IsPrayerReminderVisible && !_viewModel.IsPrayerArrivalVisible) { UpdateCompactPillDimensions(); return; }
        if (_prayerSizePending) return;
        _prayerSizePending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            _prayerSizePending = false;
            if (!_windowClosed) UpdateCompactPillDimensions();
        });
    }

    private bool UseCompactGamingReadout() => _viewModel.CompactCapsuleIconType == "Gaming" &&
        !_viewModel.IsPrayerReminderVisible && !_viewModel.IsPrayerArrivalVisible && !_viewModel.IsHardwareNoticeVisible && !_viewModel.IsNotificationInMainPill;

    private readonly MochiDock.Core.ExpansionTransition _expansionTransition = new();
    private bool _windowClosed;
    private void ApplyExpandState(bool expand)
    {
        if (_windowClosed || !_expansionTransition.Request(expand)) return;
        long generation = _expansionTransition.Generation;

        if (expand)
        {
            _volumeOverlayTimer.Stop();
            _isAdjustingVolume = false;
            CapsuleVolumeOverlay.Visibility = Visibility.Collapsed;
            CapsulePowerOverlay.Visibility = Visibility.Collapsed;
            PillNormalContent.Visibility = Visibility.Visible;
            var (w, h) = GetCardDimensions(_viewModel.ActiveCardType);
            CompactContainer.Visibility = Visibility.Collapsed;
            ExpandedBorder.Visibility = Visibility.Visible;

            var duration = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var wAnim = new DoubleAnimation(290, w, duration) { EasingFunction = ease };
            var hAnim = new DoubleAnimation(40, h, duration) { EasingFunction = ease };

            hAnim.Completed += (s, e) =>
            {
                if (_windowClosed || !_expansionTransition.Complete(generation)) return;
                if (_expansionTransition.Desired != expand) ApplyExpandState(_expansionTransition.Desired);
                else { var dimensions = GetCardDimensions(_viewModel.ActiveCardType); AnimateExpandedDimensions(dimensions.width, dimensions.height); }
            };

            ExpandedBorder.BeginAnimation(WidthProperty, wAnim);
            ExpandedBorder.BeginAnimation(HeightProperty, hAnim);
        }
        else
        {
            var duration = TimeSpan.FromMilliseconds(180);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

            double returnWidth = GetCurrentCompactTargetWidth();
            var wAnim = new DoubleAnimation(ExpandedBorder.ActualWidth, returnWidth, duration) { EasingFunction = ease };
            var hAnim = new DoubleAnimation(ExpandedBorder.ActualHeight, 40, duration) { EasingFunction = ease };

            hAnim.Completed += (s, e) =>
            {
                if (_windowClosed || !_expansionTransition.Complete(generation)) return;
                if (_expansionTransition.Desired) { ApplyExpandState(true); return; }
                ExpandedBorder.Visibility = Visibility.Collapsed;
                CompactContainer.Visibility = Visibility.Visible;
                UpdateCompactPillWidth();
            };

            ExpandedBorder.BeginAnimation(WidthProperty, wAnim);
            ExpandedBorder.BeginAnimation(HeightProperty, hAnim);
        }
    }

    private void AnimateExpandedDimensions(double targetW, double targetH)
    {
        if (_windowClosed || _expansionTransition.Running) return;
        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var wAnim = new DoubleAnimation(ExpandedBorder.ActualWidth, targetW, duration) { EasingFunction = ease };
        var hAnim = new DoubleAnimation(ExpandedBorder.ActualHeight, targetH, duration) { EasingFunction = ease };

        ExpandedBorder.BeginAnimation(WidthProperty, wAnim);
        ExpandedBorder.BeginAnimation(HeightProperty, hAnim);
    }
}
