using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsPoint = System.Windows.Point;
using WindowsColor = System.Windows.Media.Color;
using WindowsSize = System.Windows.Size;
using WindowsHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace MochiDock.Controls;

public partial class MarqueeTextBlock : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty CenterNumeralsProperty = DependencyProperty.Register(
        nameof(CenterNumerals), typeof(bool), typeof(MarqueeTextBlock), new PropertyMetadata(false, OnTextPropertyChanged));
    public bool CenterNumerals { get => (bool)GetValue(CenterNumeralsProperty); set => SetValue(CenterNumeralsProperty, value); }
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnTextPropertyChanged));

    public static readonly DependencyProperty ScrollSpeedProperty =
        DependencyProperty.Register(
            nameof(ScrollSpeed),
            typeof(double),
            typeof(MarqueeTextBlock),
            new PropertyMetadata(35.0));

    public static readonly DependencyProperty EnableScrollingProperty =
        DependencyProperty.Register(
            nameof(EnableScrolling),
            typeof(bool),
            typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnEnableScrollingChanged));

    public static readonly DependencyProperty RaiseCycleCompletedProperty =
        DependencyProperty.Register(
            nameof(RaiseCycleCompleted),
            typeof(bool),
            typeof(MarqueeTextBlock),
            new PropertyMetadata(false));

    public static readonly RoutedEvent CycleCompletedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(CycleCompleted),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(MarqueeTextBlock));

    public event RoutedEventHandler CycleCompleted
    {
        add => AddHandler(CycleCompletedEvent, value);
        remove => RemoveHandler(CycleCompletedEvent, value);
    }

    public bool RaiseCycleCompleted
    {
        get => (bool)GetValue(RaiseCycleCompletedProperty);
        set => SetValue(RaiseCycleCompletedProperty, value);
    }

    /// <summary>
    /// Seconds the current text stays on screen (a synced lyric line: the time until the next line). When it is set and the text
    /// is too wide, the text scrolls once, paced by <see cref="MochiDock.Core.LyricScrollPace"/> so its end is readable in time,
    /// and then holds instead of looping. 0 (the default) keeps the looping marquee.
    /// </summary>
    public static readonly DependencyProperty PaceSecondsProperty =
        DependencyProperty.Register(nameof(PaceSeconds), typeof(double), typeof(MarqueeTextBlock), new PropertyMetadata(0.0, OnPaceSecondsChanged));

    public double PaceSeconds
    {
        get => (double)GetValue(PaceSecondsProperty);
        set => SetValue(PaceSecondsProperty, value);
    }

    private static void OnPaceSecondsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeTextBlock control) control.ScheduleUpdate(resetScroll: false);
    }

    private readonly Stopwatch _stopwatch = new();
    private double _totalLoopDistance;
    private double _scrollDurationSeconds;
    private bool _isMarqueeActive;
    private bool _isRenderingHooked;
    private string _activeText = string.Empty;
    private double _lastViewportWidth;
    private int _singleLineCompletedCycleCount;
    private bool _fitCycleReported;
    private bool _isPaced;
    private double _pacedStartPause;
    private double _pacedSpeed;

    public MarqueeTextBlock()
    {
        InitializeComponent();

        Loaded += (s, e) => ScheduleUpdate(resetScroll: true);
        Unloaded += (s, e) => StopScroll();
        IsVisibleChanged += (s, e) => { if (IsVisible) ScheduleUpdate(resetScroll: true); else StopScroll(); };
        SizeChanged += (s, e) =>
        {
            if (Math.Abs(e.NewSize.Width - _lastViewportWidth) > 0.5)
            {
                _lastViewportWidth = e.NewSize.Width;
                ScheduleUpdate(resetScroll: true);
            }
        };
    }

    private void StartScroll()
    {
        if (!_isRenderingHooked)
        {
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            _isRenderingHooked = true;
        }
        _isMarqueeActive = true;
        _stopwatch.Restart();
    }

    private void StopScroll(bool resetPosition = true)
    {
        if (_isRenderingHooked)
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _isRenderingHooked = false;
        }
        _stopwatch.Reset();
        _isMarqueeActive = false;
        _totalLoopDistance = 0;
        _scrollDurationSeconds = 0;
        if (resetPosition)
        {
            TranslateTransform.X = 0;
            TranslateTransform.Y = 0;
        }
        if (ContentPanel != null) ContentPanel.Opacity = 1;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (PrimaryText != null &&
            (e.Property == FontFamilyProperty || e.Property == FontSizeProperty || e.Property == FontWeightProperty))
        {
            ScheduleUpdate(resetScroll: true);
        }
    }

    // Sets a line through Emoji.Text, so emoji in a message are drawn as the same coloured pictures as on the cards (WPF text draws them in
    // one colour). Cleared first, so the pictures are rebuilt at the current font size; text without emoji costs nothing extra.
    private static void SetLine(System.Windows.Controls.TextBlock block, string text)
    {
        Emoji.SetText(block, null);
        Emoji.SetText(block, text);
    }
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double ScrollSpeed
    {
        get => (double)GetValue(ScrollSpeedProperty);
        set => SetValue(ScrollSpeedProperty, value);
    }

    public bool EnableScrolling
    {
        get => (bool)GetValue(EnableScrollingProperty);
        set => SetValue(EnableScrollingProperty, value);
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeTextBlock control)
        {
            control.ScheduleUpdate(resetScroll: true);
        }
    }

    private static void OnEnableScrollingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeTextBlock control)
        {
            control.ScheduleUpdate(resetScroll: true);
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ScheduleUpdate(resetScroll: false);
    }

    private void ScheduleUpdate(bool resetScroll)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => UpdateMarquee(resetScroll)));
    }

    private void FireCycleCompleted()
    {
        if (RaiseCycleCompleted)
        {
            RaiseEvent(new RoutedEventArgs(CycleCompletedEvent, this));
        }
    }

    /// <summary>Rounds a horizontal DIP offset to a whole device pixel so glyphs never land on sub-pixel positions.</summary>
    private double SnapX(double dip)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return scale > 0 ? Math.Round(dip * scale) / scale : dip;
    }

    private void UpdateVerticalPlacement(double textHeight)
    {
        if (ViewportCanvas != null)
        {
            ViewportCanvas.Height = Math.Max(20, textHeight);
            double center = CenterNumerals ? CompactTextLine.NumeralCenter(PrimaryText) : textHeight / 2;
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
            Canvas.SetTop(ContentPanel, CenterNumerals
                ? Math.Round((ViewportCanvas.Height / 2 - center) * dpi) / dpi
                : Math.Max(0, (ViewportCanvas.Height - textHeight) / 2.0));
        }
    }

    public void UpdateMarquee(bool resetScroll = false)
    {
        if (!IsLoaded || !IsVisible || PrimaryText == null || ClippingBorder == null) return;

        bool textChanged = !string.Equals(_activeText, Text, StringComparison.Ordinal);
        if (textChanged)
        {
            _activeText = Text ?? string.Empty;
            _lines = SplitLines(_activeText);
            _lineIndex = 0;
            resetScroll = true;
            _singleLineCompletedCycleCount = 0;
            _fitCycleReported = false;
        }

        // Strictly reset any vertical translation from multi-line transitions
        TranslateTransform.Y = 0;

        if (_lines.Length > 1 && EnableScrolling)
        {
            UpdateMultiLine(resetScroll);
            return;
        }

        _isMultiLine = false;
        _isPaced = false;
        ContentPanel.Opacity = 1;
        SetLine(PrimaryText, _lines.Length == 1 ? _lines[0] : _activeText);
        SetLine(SecondaryText, _lines.Length == 1 ? _lines[0] : _activeText);

        PrimaryText.Measure(new WindowsSize(double.PositiveInfinity, double.PositiveInfinity));
        double textWidth = PrimaryText.DesiredSize.Width;
        double textHeight = PrimaryText.DesiredSize.Height;

        UpdateVerticalPlacement(textHeight);

        // Determine available viewport width
        double availableWidth = ActualWidth;
        if (availableWidth <= 0 && Parent is FrameworkElement parentElement && parentElement.ActualWidth > 0)
        {
            availableWidth = parentElement.ActualWidth;
        }
        if (availableWidth <= 0 && MaxWidth > 0 && !double.IsInfinity(MaxWidth))
        {
            availableWidth = MaxWidth;
        }

        // Wait until layout assigns a real width (> 15px) before starting or locking marquee
        if (availableWidth <= 15)
        {
            StopScroll();
            LoopPanel.Visibility = Visibility.Collapsed;
            ClippingBorder.OpacityMask = null;
            return;
        }

        // If text fits comfortably (with safety margin for sub-pixel text rendering), keep it static!
        if (!EnableScrolling || string.IsNullOrWhiteSpace(Text) || textWidth <= (availableWidth + 6))
        {
            // Static text: centered, no scrolling, no edge fade
            StopScroll();
            LoopPanel.Visibility = Visibility.Collapsed;
            ClippingBorder.OpacityMask = null;
            _totalLoopDistance = 0;
            _scrollDurationSeconds = 0;
            TranslateTransform.X = 0;

            if (ViewportCanvas != null && availableWidth > textWidth)
            {
                // Center only inside the compact pill; in expanded cards (MaxWidth set), keep left-aligned
                // True centre: the capsule's columns are symmetric around the clock (see CapsuleClockLayout), so no optical nudge is needed.
                double leftOffset = (MaxWidth > 0 && !double.IsInfinity(MaxWidth)) ? 0 : (availableWidth - textWidth) / 2.0;
                Canvas.SetLeft(ContentPanel, SnapX(leftOffset));
            }
            else if (ViewportCanvas != null)
            {
                Canvas.SetLeft(ContentPanel, 0);
            }

            if (RaiseCycleCompleted && !string.IsNullOrWhiteSpace(Text))
            {
                // Start a timer to trigger CycleCompleted for fitting text after a comfortable read period (3.5s)
                if (!_isMarqueeActive || resetScroll)
                {
                    TranslateTransform.X = 0;
                    _fitCycleReported = false;
                    StartScroll();
                }
            }
            return;
        }

        // Paced text (a lyric line with a known time on screen): one pass to the end, then hold.
        if (PaceSeconds > 0 && double.IsFinite(PaceSeconds))
        {
            if (ViewportCanvas != null) Canvas.SetLeft(ContentPanel, 0);
            LoopPanel.Visibility = Visibility.Collapsed;
            ClippingBorder.OpacityMask = null;
            double overflow = textWidth - availableWidth + 12;
            var plan = MochiDock.Core.LyricScrollPace.Plan(overflow, PaceSeconds, ScrollSpeed > 0 ? ScrollSpeed : 35.0);
            _isPaced = true;
            _pacedStartPause = plan.StartPause;
            _pacedSpeed = plan.Speed;
            _totalLoopDistance = overflow;
            _scrollDurationSeconds = plan.ScrollSeconds;
            if (resetScroll || !_isMarqueeActive)
            {
                TranslateTransform.X = 0;
                _singleLineCompletedCycleCount = 0;
                StartScroll();
            }
            return;
        }

        // Long text: Left-aligned, seamless loop, soft edge fade across the entire available slot
        if (ViewportCanvas != null)
        {
            Canvas.SetLeft(ContentPanel, 0);
        }
        LoopPanel.Visibility = Visibility.Visible;
        ClippingBorder.OpacityMask = null;

        // The duplicate must be placed after the full text AND after the full bullet separator
        SeparatorBullet.Measure(new WindowsSize(double.PositiveInfinity, double.PositiveInfinity));
        _totalLoopDistance = textWidth + SeparatorBullet.DesiredSize.Width;
        double speed = ScrollSpeed > 0 ? ScrollSpeed : 35.0;
        _scrollDurationSeconds = _totalLoopDistance / speed;

        if (resetScroll || !_isMarqueeActive)
        {
            TranslateTransform.X = 0;
            StartScroll();
        }
    }

    private static LinearGradientBrush CreateEdgeFadeBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new WindowsPoint(0, 0),
            EndPoint = new WindowsPoint(1, 0)
        };
        brush.GradientStops.Add(new GradientStop(WindowsColor.FromArgb(0, 0, 0, 0), 0.0));
        brush.GradientStops.Add(new GradientStop(WindowsColor.FromArgb(255, 0, 0, 0), 0.04));
        brush.GradientStops.Add(new GradientStop(WindowsColor.FromArgb(255, 0, 0, 0), 0.96));
        brush.GradientStops.Add(new GradientStop(WindowsColor.FromArgb(0, 0, 0, 0), 1.0));
        return brush;
    }

    // ───────────── Multi-line (per-line cycling) mode ─────────────
    private string[] _lines = Array.Empty<string>();
    private int _lineIndex;
    private bool _isMultiLine;
    private double _lineScrollDistance; // 0 when the current line fits
    private double _lineScrollSeconds;
    private double _lineCenterOffset;

    private const double LineHoldFitSeconds = 2.5;
    private const double LineStartPauseSeconds = 1.5;
    private const double LineEndPauseSeconds = 0.8;
    private const double LineFadeSeconds = 0.15;

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        return text.Replace("\r\n", "\n").Replace('\r', '\n')
                   .Split('\n')
                   .Select(l => l.Trim())
                   .Where(l => l.Length > 0)
                   .ToArray();
    }

    private double GetAvailableWidth()
    {
        double w = ActualWidth;
        if (w <= 0 && Parent is FrameworkElement p && p.ActualWidth > 0) w = p.ActualWidth;
        if (w <= 0 && MaxWidth > 0 && !double.IsInfinity(MaxWidth)) w = MaxWidth;
        return w;
    }

    private void UpdateMultiLine(bool resetScroll)
    {
        double availableWidth = GetAvailableWidth();
        if (availableWidth <= 15)
        {
            StopScroll();
            return;
        }

        _isMultiLine = true;
        LoopPanel.Visibility = Visibility.Collapsed;
        if (_lineIndex >= _lines.Length) _lineIndex = 0;
        PrepareLine(availableWidth);

        if (resetScroll || !_isMarqueeActive)
        {
            TranslateTransform.X = 0;
            StartScroll();
        }
    }

    private void PrepareLine(double availableWidth)
    {
        SetLine(PrimaryText, _lines[_lineIndex]);
        PrimaryText.Measure(new WindowsSize(double.PositiveInfinity, double.PositiveInfinity));
        double textWidth = PrimaryText.DesiredSize.Width;
        double textHeight = PrimaryText.DesiredSize.Height;

        UpdateVerticalPlacement(textHeight);

        if (textWidth > availableWidth + 6)
        {
            // Scroll exactly until the last character reaches the right edge (+ small padding for fade)
            _lineScrollDistance = textWidth - availableWidth + 12;
            double speed = ScrollSpeed > 0 ? ScrollSpeed : 35.0;
            _lineScrollSeconds = _lineScrollDistance / speed;
            _lineCenterOffset = 0;
            ClippingBorder.OpacityMask = null;
        }
        else
        {
            _lineScrollDistance = 0;
            _lineScrollSeconds = 0;
            bool isCard = MaxWidth > 0 && !double.IsInfinity(MaxWidth);
            _lineCenterOffset = isCard ? 0 : (availableWidth - textWidth) / 2.0;
            ClippingBorder.OpacityMask = null;
        }

        Canvas.SetLeft(ContentPanel, SnapX(_lineCenterOffset));
        TranslateTransform.X = 0;
    }

    private void MultiLineTick()
    {
        double t = _stopwatch.Elapsed.TotalSeconds;
        double visibleDuration = _lineScrollDistance > 0
            ? LineStartPauseSeconds + _lineScrollSeconds + LineEndPauseSeconds
            : LineHoldFitSeconds;

        // Fade-in at the start of each line
        if (t < LineFadeSeconds)
        {
            double p = t / LineFadeSeconds;
            ContentPanel.Opacity = p;
            TranslateTransform.Y = (1 - p) * 4;
        }
        else
        {
            TranslateTransform.Y = 0;
            ContentPanel.Opacity = 1;
        }

        if (_lineScrollDistance > 0)
        {
            if (t <= LineStartPauseSeconds) TranslateTransform.X = 0;
            else if (t <= LineStartPauseSeconds + _lineScrollSeconds)
                TranslateTransform.X = SnapX(-((t - LineStartPauseSeconds) / _lineScrollSeconds) * _lineScrollDistance);
            else TranslateTransform.X = SnapX(-_lineScrollDistance);
        }
        else
        {
            TranslateTransform.X = 0;
        }

        // Fade-out, then advance to next line
        if (t >= visibleDuration)
        {
            double p = Math.Min(1, (t - visibleDuration) / LineFadeSeconds);
            ContentPanel.Opacity = 1 - p;
            TranslateTransform.Y = -p * 4;
            if (p >= 1)
            {
                int prevIndex = _lineIndex;
                _lineIndex = (_lineIndex + 1) % _lines.Length;
                
                // If we just finished the last line in the multi-line sequence, signal CycleCompleted!
                if (prevIndex == _lines.Length - 1)
                {
                    TranslateTransform.Y = 0;
                    FireCycleCompleted();
                }

                PrepareLine(GetAvailableWidth());
                ContentPanel.Opacity = 0;
                TranslateTransform.Y = 0;
                _stopwatch.Restart();
            }
        }
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (!_isMarqueeActive) return;

        if (_isMultiLine)
        {
            if (_lines.Length > 1) MultiLineTick();
            return;
        }

        // Fit / static text cycle timer
        if (_totalLoopDistance <= 0 || _scrollDurationSeconds <= 0)
        {
            TranslateTransform.X = 0;
            if (RaiseCycleCompleted && !_fitCycleReported)
            {
                if (_stopwatch.Elapsed.TotalSeconds >= 3.5)
                {
                    _fitCycleReported = true;
                    FireCycleCompleted();
                    StopScroll();
                }
            }
            else
            {
                StopScroll();
            }
            return;
        }

        double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;

        if (_isPaced)
        {
            double travelled = elapsedSeconds <= _pacedStartPause ? 0 : Math.Min(_totalLoopDistance, (elapsedSeconds - _pacedStartPause) * _pacedSpeed);
            TranslateTransform.X = SnapX(-travelled);
            if (travelled >= _totalLoopDistance)
            {
                if (_singleLineCompletedCycleCount == 0)
                {
                    _singleLineCompletedCycleCount = 1;
                    FireCycleCompleted();
                }
                StopScroll(resetPosition: false);
            }
            return;
        }

        // 2.0s initial pause, then linear scroll over _scrollDurationSeconds
        double cycleDuration = 2.0 + _scrollDurationSeconds;
        int currentCycle = (int)(elapsedSeconds / cycleDuration);
        if (currentCycle > _singleLineCompletedCycleCount)
        {
            _singleLineCompletedCycleCount = currentCycle;
            FireCycleCompleted();
        }

        double cycleTime = elapsedSeconds % cycleDuration;

        if (cycleTime <= 2.0)
        {
            // Paused at the very start (letter #1 fully visible)
            TranslateTransform.X = 0;
        }
        else
        {
            // Scrolling across
            double progress = (cycleTime - 2.0) / _scrollDurationSeconds;
            TranslateTransform.X = SnapX(-(progress * _totalLoopDistance));
        }
    }
}
