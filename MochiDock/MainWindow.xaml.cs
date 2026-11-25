using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MochiDock.Models;
using MochiDock.Services;
using MochiDock.ViewModels;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;

namespace MochiDock;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    private const int HOTKEY_ID_RESOLUTION = 9002;
    private const int HOTKEY_ID_SCRIPT = 9003;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_R = 0x52;
    private const uint VK_S = 0x53;
    private const int WM_HOTKEY = 0x0312;
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private readonly MainViewModel _viewModel;
    private readonly Win32TrayIcon _trayIcon;
    private readonly DispatcherTimer _hoverExpandTimer;
    private bool _isHoverNudged;
    private bool _isDraggingDock;
    private System.Windows.Point _dragStartPoint;
    private double _dragStartLeft;

    private readonly DispatcherTimer _mouseLeaveTimer;
    // The open dock got shorter (a notification dismissed, all cleared) and the still cursor ended up outside it: that leave is not the user
    // moving away, so the dock waits (CollapseGrace) instead of closing after the usual leave delay.
    private readonly DispatcherTimer _shrinkGraceTimer;
    private DateTime _lastShrinkUtc = DateTime.MinValue;
    private DateTime _shrinkLeaveUtc;
    private System.Windows.Point _shrinkLeaveCursor;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
    private readonly DispatcherTimer _volumeOverlayTimer;
    private bool _isAdjustingVolume;
    private readonly DispatcherTimer _topmostWatchdogTimer;
    private readonly DisplayResolutionService _resolutionService = new();
    private DispatcherTimer? _resolutionNoticeTimer;
    private HwndSource? _hwndSource;
    private MochiDock.Services.TaskbarReadoutWidget? _batteryWidget;   // the taskbar readout (blocks beside the tray; named for the device batteries it started as)
    private readonly DispatcherTimer _takeoverTimeoutTimer;
    private DateTime _takeoverStart = DateTime.MinValue;
    private AppSettings _settings;

    private bool _suppressHoverExpandUntilLeave;
    private DateTime _lastManualCloseTime = DateTime.MinValue;
    private ContextMenu? _trayContextMenu;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _settings = _viewModel.Settings;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.SettingsChanged += OnSettingsChanged;
        _viewModel.ResolutionPresetRequested += ApplyResolutionPreset;
        _viewModel.GameProfileResolutionRequested += ApplyGameProfileResolution;
        _viewModel.RecentWhatsAppMessages.CollectionChanged += (s, e) =>
        {
            if (_viewModel.ActiveCardType == IslandCardType.WhatsApp && (_viewModel.IsExpanded || ExpandedBorder.Visibility == Visibility.Visible))
            {
                var (w, h) = GetCardDimensions(IslandCardType.WhatsApp);
                AnimateExpandedDimensions(w, h);
            }
        };

        CardTransitions.CardFactory = CreateCardForKey;

        _viewModel.RecentEmailMessages.CollectionChanged += (s, e) =>
        {
            if (_viewModel.ActiveCardType == IslandCardType.Email && (_viewModel.IsExpanded || ExpandedBorder.Visibility == Visibility.Visible))
            {
                var (w, h) = GetCardDimensions(IslandCardType.Email);
                AnimateExpandedDimensions(w, h);
            }
        };

        // Grace period timer before auto-collapsing on mouse leave
                // Hover pre-expand delay: a 320 ms dwell so passing the pointer over the capsule does not open it
        _hoverExpandTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(320)
        };
        _hoverExpandTimer.Tick += (s, e) =>
        {
            _hoverExpandTimer.Stop();
            if (_viewModel.IsHardwareNoticeVisible) return;
            if (_viewModel.IsPrayerReminderVisible && PrayerReminderContent.IsAcknowledgeHovered) return;
            _isHoverNudged = false;
            CompactPillBorder.BeginAnimation(HeightProperty, null);
            CompactPillBorder.Height = GetCurrentCompactTargetHeight();
            if (!_viewModel.IsExpanded)
            {
                _viewModel.PrepareHoverExpand();
                _viewModel.IsExpanded = true;
            }
        };

        _viewModel.IsDockInteractionActive = () => _viewModel.IsConfirmingUpdate || ExpandedBorder.IsMouseOver || CompactContainer.IsMouseOver || IsMouseCaptureWithin || System.Windows.Input.Mouse.Captured != null || (ContextMenu?.IsOpen ?? false);
        _mouseLeaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _mouseLeaveTimer.Tick += (s, e) =>
        {
            _mouseLeaveTimer.Stop();
            if(!_viewModel.IsPinned&&_viewModel.IsExpanded&&(_viewModel.IsDockInteractionActive?.Invoke()??false)){_viewModel.IsAutoCollapseDeferred=true;return;}
            if (!_viewModel.IsPinned && _viewModel.IsExpanded && !(_viewModel.IsDockInteractionActive?.Invoke()??false))
            {
                _viewModel.IsExpanded = false;
            }
        };

        _shrinkGraceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _shrinkGraceTimer.Tick += (_, _) => ShrinkGraceTick();
        ExpandedBorder.SizeChanged += (_, e) =>
        {
            if (e.HeightChanged && e.NewSize.Height < e.PreviousSize.Height - 1) _lastShrinkUtc = DateTime.UtcNow;
        };

        // Safety timeout timer for notification marquee takeover during media (12s max)
        _takeoverTimeoutTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _takeoverTimeoutTimer.Tick += (s, e) =>
        {
            if (_viewModel.IsNotificationTakeover)
            {
                if ((DateTime.UtcNow - _takeoverStart).TotalSeconds >= 12.0)
                {
                    _takeoverTimeoutTimer.Stop();
                    _viewModel.EndNotificationTakeover();
                }
            }
            else
            {
                _takeoverTimeoutTimer.Stop();
            }
        };

        // Volume overlay auto-hide timer (2.6s)
        _volumeOverlayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2600)
        };
        _volumeOverlayTimer.Tick += (s, e) =>
        {
            _volumeOverlayTimer.Stop();
            _isAdjustingVolume = false;
            CapsuleVolumeOverlay.Visibility = Visibility.Collapsed;
            PillNormalContent.Visibility = Visibility.Visible;
            UpdateCompactPillWidth();
        };

        _viewModel.AudioService.MasterVolumeChanged += OnMasterVolumeChanged;

        // Load authentic Mochi Icon for System Tray
        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
        _trayIcon = new Win32TrayIcon(iconPath, "Mochi Dock") { Visible = true };
        _trayIcon.DoubleClick += () => Dispatcher.Invoke(() => _viewModel.IsExpanded = !_viewModel.IsExpanded);
        _trayIcon.RightClick += () => Dispatcher.Invoke(() =>
        {
            try { ShowModernTrayMenu(); }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Window", logEx); }
        });
        // A reminder the hidden dock cannot show (a fullscreen app is in front): a Windows notification from the tray icon.
        _viewModel.ReminderFallbackRequested += (title, text) =>
        {
            try { _trayIcon.ShowBalloonTip(8000, title, text); }
            catch (System.Exception ex) { MochiDock.Services.AppLog.Warn("Window", ex); }
        };

        Loaded += MainWindow_Loaded;
        // Top banners sit above the card: when shown, hidden or rewrapped, the open dock re-sizes to what is really there.
        FirstRunWelcomeBanner.IsVisibleChanged += (_, _) => ResizeForBanners();
        FirstRunWelcomeBanner.SizeChanged += (_, e) => { if (e.HeightChanged) ResizeForBanners(); };
        UpdateBanner.IsVisibleChanged += (_, _) => ResizeForBanners();
        UpdateBanner.SizeChanged += (_, e) => { if (e.HeightChanged) ResizeForBanners(); };
        Loaded += (_,_)=>{if(_powerNotifications!=null)return;_powerNotifications=new(new WindowInteropHelper(this).Handle);_powerNotifications.DisplayChanged+=OnConsoleDisplayChanged;_powerNotifications.PowerChanged+=OnSystemPowerChanged;_viewModel.PowerNoticeRequested+=ShowPowerNotice;_viewModel.OnPowerState(MochiDock.Services.BatteryStatus.Read());_policyTimer.Tick+=PolicyTick;_policyTimer.Start();};
        Closed += (_,_)=>{_batteryWidget?.Dispose();_batteryWidget=null;_powerNoticeTimer?.Stop();_viewModel.PowerNoticeRequested-=ShowPowerNotice;_viewModel.AudioService.MasterVolumeChanged-=OnMasterVolumeChanged;_viewModel.AudioService.Dispose();HeaderPillOverlay.Cancel();_viewModel.StopPresentationTimers();_windowClosed=true;_expansionTransition.Invalidate();_hoverExpandTimer.Stop();_mouseLeaveTimer.Stop();_shrinkGraceTimer.Stop();_takeoverTimeoutTimer.Stop();_volumeOverlayTimer?.Stop();_topmostWatchdogTimer?.Stop();SystemEvents.SessionSwitch-=SystemEvents_SessionSwitch;SystemEvents.DisplaySettingsChanged-=SystemEvents_DisplaySettingsChanged;_resolutionNoticeTimer?.Stop();_viewModel.IsDockInteractionActive=null;_viewModel.StopHardwareCapture();_policyTimer.Stop();_policyTimer.Tick-=PolicyTick;if(_powerNotifications!=null){_powerNotifications.DisplayChanged-=OnConsoleDisplayChanged;_powerNotifications.PowerChanged-=OnSystemPowerChanged;_powerNotifications.Dispose();_powerNotifications=null;}};

        Closing += MainWindow_Closing;

        // Listen for Windows Workstation Lock / Unlock events (Background mode & Good Morning briefing)
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;

        // Recalibrate window position on monitor wake-up or display resolution changes
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;

        // Anti-disappearance watchdog timer: keep MochiDock pinned to HWND_TOPMOST
        _topmostWatchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _topmostWatchdogTimer.Tick += (s, e) => EnsureWindowTopmost();
        _topmostWatchdogTimer.Start();
    }

    public void ApplyDockStyle(bool isNotch)
    {
        CompactPillBorder.IsNotch = isNotch;
        ExpandedBorder.IsNotch = isNotch;
        if (isNotch)
        {
            CompactContainer.Margin = new Thickness(9, 0, 9, 0);
            CompactPillBorder.CornerRadius = new CornerRadius(0, 0, 20, 20);
            ExpandedBorder.Margin = new Thickness(9, 0, 9, 0);
            ExpandedBorder.CornerRadius = new CornerRadius(0, 0, 20, 20);
            CompactPillBorder.BorderThickness = new Thickness(1.2, 0, 1.2, 1.2);
            ExpandedBorder.BorderThickness = new Thickness(1.2, 0, 1.2, 1.2);
        }
        else
        {
            CompactContainer.Margin = new Thickness(0, 4, 0, 0);
            CompactPillBorder.CornerRadius = new CornerRadius(22);
            ExpandedBorder.Margin = new Thickness(0, 4, 0, 0);
            ExpandedBorder.CornerRadius = new CornerRadius(20);
            CompactPillBorder.BorderThickness = new Thickness(1.2);
            ExpandedBorder.BorderThickness = new Thickness(1.2);
        }
    }

    private void EnsureWindowTopmost()
    {
        if(_policyHidden)return;
        try
        {
            if (!Topmost) Topmost = true;
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Window", logEx); }
    }

    // The console display state from Windows (physical monitors) and the display state last handed to the view model. In a Remote
    // Desktop session the physical monitors are usually off while the user works on the remote screen: LocalFeaturePolicy.DisplayAwake
    // then counts the display as on. Re-evaluated when a remote session connects or disconnects.
    private bool _consoleDisplayOn = true;
    private bool? _appliedDisplayAwake;

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private static bool IsRemoteSession() => GetSystemMetrics(0x1000) != 0;   // SM_REMOTESESSION

    private void OnConsoleDisplayChanged(bool on)
    {
        _consoleDisplayOn = on;
        ApplyDisplayAwake();
    }

    private void ApplyDisplayAwake()
    {
        bool awake = MochiDock.Core.LocalFeaturePolicy.DisplayAwake(_consoleDisplayOn, IsRemoteSession());
        if (_appliedDisplayAwake == awake) return;   // the view model resets sampling baselines, so only on a real change
        _appliedDisplayAwake = awake;
        _viewModel.SetDisplayAwake(awake);
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_windowClosed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_windowClosed) PositionWindow();
        });
    }

    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (_windowClosed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_windowClosed) return;
            if (e.Reason is SessionSwitchReason.RemoteConnect or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.ConsoleDisconnect)
                ApplyDisplayAwake();
            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                CardTransitions.Settle();
                _viewModel.HandleSessionLock();
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                _viewModel.HandleSessionUnlock();
                PositionWindow();
            }
            else if (e.Reason == SessionSwitchReason.RemoteConnect)
            {
                PositionWindow();
            }
        });
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PreviewKeyDown -= MainWindow_PreviewKeyDown;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        // Exclude MochiDock from Windows Alt+Tab switcher and initialize global hotkeys
        try
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

                _hwndSource = HwndSource.FromHwnd(hwnd);
                _hwndSource?.AddHook(HwndMessageHook);
                _trayIcon.Attach(hwnd);
                ApplyResolutionHotkey();
                RegisterHotKey(hwnd, HOTKEY_ID_SCRIPT, MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, VK_S);
                ApplyWindowSizeShortcuts();
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Window", logEx); }

        try
        {
            _batteryWidget = new MochiDock.Services.TaskbarReadoutWidget(_viewModel);
            _batteryWidget.ContextMenuRequested += ShowTaskbarWidgetMenu;
            _batteryWidget.BlockClicked += kind => _viewModel.OpenTaskbarBlock(kind);   // Stats (CPU, Memory, Network), Prayer, Weather or the Battery page
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Window", logEx); }

        _viewModel.ExcludeFromScreenCaptureChanged += ApplyScreenCaptureAffinity;
        ApplyScreenCaptureAffinity(_viewModel.ExcludeFromScreenCapture);

        PositionWindow();
        UpdateCompactPillWidth();
        _viewModel.ClipboardServiceInstance.Initialize(this);
        ThemeService.ApplyTheme(this, _viewModel.CapsuleTheme);
        if (_viewModel.IsFirstRunWelcomeVisible) _viewModel.ShowFirstRunCapsuleNotice();

        _ = Task.Run(async () =>
        {
            try
            {
                bool startWithWindows = await StartupService.InitializeAsync();
                if (!_windowClosed && !Dispatcher.HasShutdownStarted)
                {
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        if (_windowClosed) return;
                        if (_settings.StartWithWindows != startWithWindows)
                        {
                            _settings.StartWithWindows = startWithWindows;
                            SettingsService.Save(_settings);
                        }
                    });
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Startup initialization: " + ex.Message); }
        });
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        App.CloseSettingsWindow(); // commit pending edits before the final flush below
        if (_hwndSource != null)
        {
            try
            {
                var h = new WindowInteropHelper(this);
                if (h.Handle != IntPtr.Zero)
                {
                    UnregisterHotKey(h.Handle, HOTKEY_ID_RESOLUTION);
                    UnregisterHotKey(h.Handle, HOTKEY_ID_SCRIPT);
                    UnregisterWindowSizeShortcuts(h.Handle);
                }
                _hwndSource.RemoveHook(HwndMessageHook);
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Window", logEx); }
        }
        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        // Final settings flush using latest settings from ViewModel
        var latestSettings = _viewModel.Settings ?? SettingsService.Load();
        latestSettings.IsStatsPinnedToCapsule = _viewModel.IsStatsPinnedToCapsule;
        latestSettings.IsExpandedPinned = _viewModel.IsPinned;

        SettingsService.Save(latestSettings);
    }

    // Unscaled size of the dock window (it must hold the largest card, the compact pill and its companion bubble). The real window is this times the interface scale.
    private const double BaseWindowWidth = 680, BaseWindowHeight = 450;
    private MochiDock.Core.InterfaceScaleResult _interfaceScale = new(MochiDock.Core.InterfaceScale.Default, 1.0, false);

    /// <summary>
    /// Fits the interface size to the target display: sets the content zoom (LayoutTransform on ScaleRoot) and the window size, and shares the
    /// zoom with tooltips, combo-box drop-downs and the Advanced Settings window through the IslandInterfaceScaleTransform resource.
    /// Cheap when nothing changed, so every path that positions the window (startup, monitor switch, DPI or display change) calls it first.
    /// </summary>
    private void ApplyInterfaceScale(MonitorScreen targetScreen, double dpiX, double dpiY)
    {
        var work = targetScreen.WorkingArea;
        var result = MochiDock.Core.InterfaceScale.Effective(_settings.InterfaceScalePercent, work.Width / dpiX, work.Height / dpiY, BaseWindowWidth, BaseWindowHeight, _settings.TopMargin);
        _viewModel.PublishEffectiveInterfaceScale(result);
        double width = BaseWindowWidth * result.Scale, height = BaseWindowHeight * result.Scale;
        if (result.Percent == _interfaceScale.Percent && Math.Abs(Width - width) < 0.01 && Math.Abs(Height - height) < 0.01) return;
        _interfaceScale = result;
        InterfaceScaleTransform.ScaleX = InterfaceScaleTransform.ScaleY = result.Scale;
        Width = width;
        Height = height;
        // A new instance (not an edit of the old one) so every DynamicResource user, in every window, picks it up.
        System.Windows.Application.Current.Resources["IslandInterfaceScaleTransform"] = new System.Windows.Media.ScaleTransform(result.Scale, result.Scale);
    }

    private void PositionWindow()
    {
        _settings = _viewModel.Settings ?? SettingsService.Load();
        var targetScreen = MultiMonitorHelper.GetTargetScreen(_settings.TargetMonitor);
        if (targetScreen == null || targetScreen.WorkingArea.Width <= 0 || targetScreen.WorkingArea.Height <= 0) return;
        var (dpiX, dpiY) = MultiMonitorHelper.GetDpiScale(targetScreen, this);
        ApplyInterfaceScale(targetScreen, dpiX, dpiY);
        double screenLeftDip = targetScreen.WorkingArea.Left / dpiX;
        double screenWidthDip = targetScreen.WorkingArea.Width / dpiX;

        bool isNotch = string.Equals(_settings.DockStyle, "Notch", StringComparison.OrdinalIgnoreCase);
        ApplyDockStyle(isNotch);

        double normalTop = isNotch ? (targetScreen.Bounds.Top / dpiY) : MultiMonitorHelper.GetTopY(this, targetScreen, topMargin: _settings.TopMargin);
        double leftDip = MultiMonitorHelper.RoundToDevicePixel(MochiDock.Core.SpotPolicy.ComputeWindowLeftDip(screenLeftDip, screenWidthDip, this.Width, _settings.IslandHorizontalRatio), dpiX);

        this.BeginAnimation(Window.TopProperty, null);
        double topDip;
        if (_viewModel.IsGamingSessionActive)
        {
            double offset = isNotch ? 0 : _settings.EffectiveGamingTopOffset;
            topDip = MultiMonitorHelper.RoundToDevicePixel(normalTop + offset, dpiY);
            SetGamingClickThrough(true);
        }
        else
        {
            topDip = MultiMonitorHelper.RoundToDevicePixel(normalTop, dpiY);
            SetGamingClickThrough(false);
        }

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, (int)Math.Round(leftDip * dpiX), (int)Math.Round(topDip * dpiY), 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }
        else
        {
            this.Left = leftDip;
            this.Top = topDip;
        }

        EnsureWindowTopmost();
    }

    private void ApplyScreenCaptureAffinity(bool exclude)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                SetWindowDisplayAffinity(helper.Handle, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Window", logEx); }
    }

    public void SwitchMonitor(MonitorOption option)
    {
        // If primary monitor is selected, store empty string so it automatically adapts if the app runs on a different PC.
        // Through UpdateSettings (not a direct write) so the quick settings card and Advanced Settings are told, and the window repositions.
        string device = option.IsPrimary ? "" : option.DeviceName;
        _viewModel.UpdateSettings(x => x.TargetMonitor = device, SettingsApply.Reposition);
        PositionWindow();
    }

    // Every expanded card uses the same fixed width (620 DIPs).
    // Grows with the unread list (one email fits in 230; each extra row needs about 65 DIPs) and for the verification-code banner.
    private double EmailCardHeight()
    {
        int count = _viewModel.RecentEmailMessages.Count;
        double height = 230 + Math.Min(Math.Max(count - 1, 0), 3) * 65;
        if (_viewModel.CurrentEmail?.HasVerificationCode == true) height += 36;
        return height;
    }

    // Space the expanded island uses around the card itself (header row, padding), measured from the live layout.
    private double _cardChrome;

    // As tall as the provider list (the card scrolls past 360); 360 until the list has been laid out once.
    private Views.Cards.AiUsageCardView? _aiUsageCard;

    private FrameworkElement? CreateCardForKey(string key) => key switch
    {
        "Pomodoro" => new Views.Cards.PomodoroCardView(),
        "Prayer" => new Views.Cards.PrayerCardView(),
        "Weather" => new Views.Cards.WeatherCardView(),
        "Media" => new Views.Cards.MediaCardView(),
        "WhatsApp" => new Views.Cards.WhatsAppCardView(),
        "Discord" => new Views.Cards.DiscordCardView(),
        "Apps" => new Views.Cards.AppsCardView(),
        "Email" => new Views.Cards.EmailCardView(),
        "Settings" => new Views.Cards.SettingsCardView(),
        "FxRates" => new Views.Cards.FxRatesCardView(),
        "Today" => new Views.Cards.TodayCardView(),
        "Hardware" => new Views.Cards.HardwareCardView(),
        "AiUsage" => CreateAiUsageCard(),
        "Script" => new Views.Cards.ScriptCardView(),
        "Downloads" => new Views.Cards.DownloadsCardView(),
        "Summary" => new Views.Cards.SummaryCardView(),
        "FileShelf" => new Views.Cards.FileShelfCardView(),
        "Clipboard" => new Views.Cards.ClipboardCardView(),
        _ => null
    };

    private Views.Cards.AiUsageCardView CreateAiUsageCard()
    {
        var card = new Views.Cards.AiUsageCardView();
        _aiUsageCard = card;
        card.PreferredHeightChanged += () =>
        {
            if (!_viewModel.IsExpanded || _viewModel.ActiveCardType != IslandCardType.AiUsage) return;
            var (w, h) = GetCardDimensions(IslandCardType.AiUsage);
            AnimateExpandedDimensions(w, h);
        };
        return card;
    }

    private double AiUsageCardHeight()
    {
        if (ExpandedBorder.ActualHeight > 0 && CardTransitions.ActualHeight > 0 && ExpandedBorder.ActualHeight > CardTransitions.ActualHeight)
            _cardChrome = ExpandedBorder.ActualHeight - CardTransitions.ActualHeight;
        if (_cardChrome <= 0 || _aiUsageCard is not { PreferredHeight: > 0 }) return 360;
        return Math.Clamp(Math.Ceiling(_cardChrome + _aiUsageCard.PreferredHeight), 200, 360);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CapsuleTheme))
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                if (_windowClosed) return;
                UpdateCompactPillDimensions();
                QueueHeaderSelectionReveal();
            });
        }
        if (e.PropertyName == nameof(MainViewModel.IsExpanded) || e.PropertyName == nameof(MainViewModel.EffectiveGamingClickThrough)) SetGamingClickThrough(_viewModel.IsGamingSessionActive);
        if (e.PropertyName == nameof(MainViewModel.IsExpanded))
        {
            if (!_viewModel.IsExpanded) { CardTransitions.Settle(); HeaderPillOverlay.Cancel(); }
            ApplyExpandState(_viewModel.IsExpanded);
            if (_viewModel.IsExpanded)
            {
                QueueHeaderSelectionReveal();
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.ActiveCardType) || e.PropertyName == nameof(MainViewModel.IsFirstRunWelcomeVisible) || e.PropertyName == nameof(MainViewModel.IsUpdateBannerShown))
        {
            QueueHeaderSelectionReveal();
            if (_viewModel.IsExpanded || ExpandedBorder.Visibility == Visibility.Visible)
            {
                var (w, h) = GetCardDimensions(_viewModel.ActiveCardType);
                AnimateExpandedDimensions(w, h);
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsGamingSessionActive))
        {
            AnimateGamingVerticalOffset(_viewModel.IsGamingSessionActive);
        }
        else if (e.PropertyName == nameof(MainViewModel.Settings))
        {
            _settings = _viewModel.Settings;
            PositionWindow();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsNotificationTakeover))
        {
            if (_viewModel.IsNotificationTakeover)
            {
                _takeoverStart = DateTime.UtcNow;
                _takeoverTimeoutTimer.Stop();
                _takeoverTimeoutTimer.Start();
            }
            else
            {
                _takeoverTimeoutTimer.Stop();
            }
            UpdateCompactPillWidth();
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentMedia) ||
                 e.PropertyName == nameof(MainViewModel.CurrentEmail) ||
                 e.PropertyName == nameof(MainViewModel.CurrentWhatsApp) ||
                 e.PropertyName == nameof(MainViewModel.CurrentDiscord) ||
                 e.PropertyName == nameof(MainViewModel.CurrentApp) ||
                 e.PropertyName == nameof(MainViewModel.CompactCapsuleText) ||
                 e.PropertyName == nameof(MainViewModel.IsCompactTextVisible) ||
                 e.PropertyName == nameof(MainViewModel.IsStatsPinnedToCapsule) ||
                 e.PropertyName == nameof(MainViewModel.IsStatsShownInCapsule) ||
                 e.PropertyName == nameof(MainViewModel.CompactCapsuleIconType) ||
                 e.PropertyName == nameof(MainViewModel.HasSingleNotification) ||
                 e.PropertyName == nameof(MainViewModel.HasCompactLyricRomaji) ||
                 e.PropertyName == nameof(MainViewModel.IsWeatherInMainPill) ||
                 e.PropertyName == nameof(MainViewModel.IsPrayerReminderVisible) ||
                 e.PropertyName == nameof(MainViewModel.IsWeatherInPrayerReminder) ||
                 e.PropertyName == nameof(MainViewModel.IsPrayerArrivalVisible) ||
                 e.PropertyName == nameof(MainViewModel.PrayerArrivalTitle) ||
                 e.PropertyName == nameof(MainViewModel.PrayerArrivalSubtitle) ||
                 e.PropertyName == nameof(MainViewModel.PrayerReminderText) ||
                 e.PropertyName == nameof(MainViewModel.WeatherTemperatureBadge) ||
                 e.PropertyName == nameof(MainViewModel.CurrentPrayerSchedule) ||
                 e.PropertyName == nameof(MainViewModel.IsPomodoroInMainPill) ||
                 e.PropertyName == nameof(MainViewModel.IsBothPomodoroAndWeatherInMainPill) ||
                 e.PropertyName == nameof(MainViewModel.IsNotificationInMainPill) ||
                 e.PropertyName == nameof(MainViewModel.IsHardwareNoticeVisible) ||
                 e.PropertyName == nameof(MainViewModel.IsUpdateNudgeVisible))
        {
            UpdateCompactPillWidth();
        }
    }

    private void AnimateGamingVerticalOffset(bool isGaming)
    {
        _settings = _viewModel.Settings ?? SettingsService.Load();
        var targetScreen = MultiMonitorHelper.GetTargetScreen(_settings.TargetMonitor);
        if (targetScreen == null || targetScreen.WorkingArea.Width <= 0 || targetScreen.WorkingArea.Height <= 0) return;
        var (dpiX, dpiY) = MultiMonitorHelper.GetDpiScale(targetScreen, this);
        ApplyInterfaceScale(targetScreen, dpiX, dpiY);
        bool isNotch = string.Equals(_settings.DockStyle, "Notch", StringComparison.OrdinalIgnoreCase);
        ApplyDockStyle(isNotch);

        double normalTop = isNotch ? (targetScreen.Bounds.Top / dpiY) : MultiMonitorHelper.GetTopY(this, targetScreen, topMargin: _settings.TopMargin);
        double offset = isNotch ? 0 : _settings.EffectiveGamingTopOffset;
        double targetTop = MultiMonitorHelper.RoundToDevicePixel(isGaming ? (normalTop + offset) : normalTop, dpiY);

        double screenLeftDip = targetScreen.WorkingArea.Left / dpiX;
        double screenWidthDip = targetScreen.WorkingArea.Width / dpiX;
        double targetLeft = MochiDock.Core.SpotPolicy.ComputeWindowLeftDip(screenLeftDip, screenWidthDip, this.Width, _settings.IslandHorizontalRatio);
        this.Left = MultiMonitorHelper.RoundToDevicePixel(targetLeft, dpiX);

        // Reset any internal translate or prior Top animation so window coordinates dictate the real position
        this.BeginAnimation(Window.TopProperty, null);
        RootVerticalTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        RootVerticalTranslate.Y = 0;

        var duration = TimeSpan.FromMilliseconds(250);
        var anim = new DoubleAnimation(this.Top, targetTop, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (s, e) =>
        {
            this.BeginAnimation(Window.TopProperty, null);
            this.Top = targetTop;
            EnsureWindowTopmost();
        };
        this.BeginAnimation(Window.TopProperty, anim);

        // Apply click-through when gaming so user cannot accidentally click or expand the island
        SetGamingClickThrough(isGaming);
    }

    private void SetGamingClickThrough(bool isGaming)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                if (MochiDock.Core.SolarPosition.ClickThrough(isGaming, _viewModel.IsExpanded, _viewModel.EffectiveGamingClickThrough))
                {


                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                    IsHitTestVisible = false;
                }
                else
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle & ~WS_EX_TRANSPARENT & ~WS_EX_NOACTIVATE) | WS_EX_TOOLWINDOW);
                    IsHitTestVisible = true;
                }
                EnsureWindowTopmost();
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Window", logEx); }
    }

    private IntPtr HwndMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_trayIcon.ProcessMessage(msg, wParam, lParam))
        {
            handled = true;
            return IntPtr.Zero;
        }
        // Device arrival/removal (DBT_DEVNODES_CHANGED) and resume (PBT_APMRESUMEAUTOMATIC) refresh the wireless battery list.
        if ((msg == 0x0219 && wParam.ToInt32() == 0x0007) || (msg == 0x0218 && wParam.ToInt32() == 0x0012))
            _viewModel.RequestDeviceBatteryRefresh(TimeSpan.FromSeconds(2));
        if (msg == WM_HOTKEY && HandleWindowSizeHotkey(wParam.ToInt32()))
        {
            handled = true;
        }
        else if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID_RESOLUTION)
        {
            handled = true;
            Dispatcher.Invoke(ToggleResolutionFromHotkey);
        }
        else if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID_SCRIPT)
        {
            handled = true;
            Dispatcher.Invoke(() =>
            {
                if (_viewModel.IsExpanded && _viewModel.ActiveCardType == IslandCardType.Script)
                {
                    _viewModel.IsExpanded = false;
                }
                else
                {
                    _viewModel.ClearTransientCompanionView();
                    _viewModel.ShowScript();
                }
            });
        }
        return IntPtr.Zero;
    }

    // Registers, moves or removes the resolution hotkey to match the current settings; safe to call repeatedly.
    private void ApplyResolutionHotkey()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            UnregisterHotKey(hwnd, HOTKEY_ID_RESOLUTION);
            var settings = _viewModel.Settings;
            if (!settings.EnableResolutionHotkey) { _viewModel.ResolutionHotkeyProblem = ""; return; }

            string? problem = null;
            if (!MochiDock.Core.HotkeyParser.TryParse(settings.ResolutionHotkey, out var hotkey, out string? parseError))
            {
                // A damaged value falls back to the default so the feature keeps working.
                MochiDock.Core.HotkeyParser.TryParse(MochiDock.Core.HotkeyParser.Default, out hotkey, out _);
                problem = parseError + " Using " + hotkey.Text + ".";
            }
            if (!RegisterHotKey(hwnd, HOTKEY_ID_RESOLUTION, hotkey.Modifiers | MOD_NOREPEAT, hotkey.VirtualKey))
            {
                problem = hotkey.Text + " is already used by another application. Choose a different shortcut.";
                MochiDock.Services.AppLog.Warn("Window", new InvalidOperationException(problem));
            }
            _viewModel.ResolutionHotkeyProblem = problem ?? "";
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Window", logEx); }
    }

    private void OnSettingsChanged(SettingsApply effects)
    {
        if (effects.HasFlag(SettingsApply.Hotkey))
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(hwnd, HOTKEY_ID_RESOLUTION);
                UnregisterWindowSizeShortcuts(hwnd);
            }
            ApplyResolutionHotkey();
            ApplyWindowSizeShortcuts();
        }
        // A new interface size resizes the window around its centre-top; the dock keeps its state (expanded stays expanded on the same card).
        if (effects.HasFlag(SettingsApply.Scale)) PositionWindow();
    }

    private void ApplyResolutionPreset(int width, int height, string label)
    {
        var screen = MultiMonitorHelper.GetTargetScreen(_viewModel.Settings.TargetMonitor);
        if (!_resolutionService.ApplyResolution(width, height, screen?.DeviceName)) return;
        PositionWindow();
        ShowResolutionNotice(label);
    }

    // A game profile's resolution: remember what was set before, and go back only while the profile's resolution is still in use.
    private (int Width, int Height)? _resolutionBeforeProfile;
    private (int Width, int Height)? _resolutionSetByProfile;

    private void ApplyGameProfileResolution(int width, int height, bool apply)
    {
        var screen = MultiMonitorHelper.GetTargetScreen(_viewModel.Settings.TargetMonitor);
        var current = _resolutionService.GetCurrentMode(screen?.DeviceName);
        if (current == null) return;

        if (apply)
        {
            if (MochiDock.Core.GameProfiles.ResolutionToApply(new MochiDock.Core.GameProfile { Resolution = $"{width}x{height}" }, current.Width, current.Height) == null) return;
            var before = (current.Width, current.Height);
            if (!_resolutionService.ApplyResolution(width, height, screen?.DeviceName)) return;
            _resolutionBeforeProfile = before;
            _resolutionSetByProfile = (width, height);
            PositionWindow();
            ShowResolutionNotice(MochiDock.Core.DisplayResolutionPolicy.FormatLabel(width, height));
            return;
        }

        if (_resolutionBeforeProfile is not { } back || _resolutionSetByProfile is not { } applied) return;
        _resolutionBeforeProfile = _resolutionSetByProfile = null;
        if (MochiDock.Core.GameProfiles.ResolutionToRestore(back, applied, current.Width, current.Height) is not { } target) return;
        if (!_resolutionService.ApplyResolution(target.Width, target.Height, screen?.DeviceName)) return;
        PositionWindow();
        ShowResolutionNotice(MochiDock.Core.DisplayResolutionPolicy.FormatLabel(target.Width, target.Height));
    }

    /// <summary>The dock monitor's current size, its native size (largest mode) and the gaming size it offers; null where unknown.</summary>
    private ((int Width, int Height)? Current, (int Width, int Height)? Native, (int Width, int Height)? Gaming) GetResolutionTargets()
    {
        var screen = MultiMonitorHelper.GetTargetScreen(_settings.TargetMonitor);
        var current = _resolutionService.GetCurrentMode(screen?.DeviceName);
        var modes = _resolutionService.GetSupportedModes(screen?.DeviceName).Select(m => (m.Width, m.Height)).ToList();
        (int Width, int Height)? cur = current == null ? null : (current.Width, current.Height);
        if (modes.Count == 0) return (cur, null, null);
        var native = MochiDock.Core.DisplayResolutionPolicy.PickNative(modes, 0, 0);
        var gaming = MochiDock.Core.GamingResolutionOptions.Resolve(MochiDock.Core.GamingResolutionOptions.Build(modes), native, _settings.PreferredGamingResolution);
        return (cur, native, gaming == null ? null : (gaming.Width, gaming.Height));
    }

    private void ToggleResolutionFromHotkey()
    {
        var screen = MultiMonitorHelper.GetTargetScreen(_settings.TargetMonitor);
        if (GetResolutionTargets().Gaming is not { } gaming)
        {
            // Never switch to a mode this display does not offer (for example a setting copied from another PC).
            ShowResolutionNotice("Pick a gaming resolution in Settings");
            return;
        }
        // The way back is what the monitor had before the toggle (or its native mode): never the screen's current size.
        var (success, newW, newH, freq) = _resolutionService.ToggleResolution(
            $"{gaming.Width}x{gaming.Height}",
            deviceName: screen?.DeviceName);

        if (success)
        {
            PositionWindow();
            string ratio = MochiDock.Core.DisplayResolutionPolicy.GetAspectRatio(newW, newH);
            ShowResolutionNotice($"{newW}×{newH} ({ratio}) @ {freq}Hz");
        }
    }

    private double _lastResolutionNoticeWidth = 180.0;

    private double MeasureResolutionNoticeWidth(string text)
    {
        var fontFamily = CapsuleResolutionText.FontFamily ?? (System.Windows.Media.FontFamily)FindResource("IslandUiFont");
        var typeface = new System.Windows.Media.Typeface(fontFamily, CapsuleResolutionText.FontStyle, CapsuleResolutionText.FontWeight, CapsuleResolutionText.FontStretch);
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var formatted = new System.Windows.Media.FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            CapsuleResolutionText.FontSize > 0 ? CapsuleResolutionText.FontSize : 11.5,
            System.Windows.Media.Brushes.Black,
            dpi);

        // Monitor icon (14 DIPs) + right margin (8 DIPs) = 22 DIPs
        // Symmetrical left and right pill edge padding (18 DIPs each side) = 36 DIPs
        double requiredWidth = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace + 22 + 36);
        return Math.Max(160, requiredWidth);
    }

    public void ShowResolutionNotice(string message)
    {
        if (_windowClosed) return;

        PositionWindow();

        CapsuleResolutionText.Text = message;
        double targetW = MeasureResolutionNoticeWidth(message);
        _lastResolutionNoticeWidth = targetW;

        double currentW = CompactPillBorder.ActualWidth > 0 ? CompactPillBorder.ActualWidth : CompactPillBorder.Width;
        if (double.IsNaN(currentW) || currentW <= 0) currentW = 125;

        if (Math.Abs(currentW - targetW) > 1)
        {
            var wAnim = new DoubleAnimation(currentW, targetW, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CompactPillBorder.BeginAnimation(WidthProperty, wAnim);
        }
        else
        {
            CompactPillBorder.BeginAnimation(WidthProperty, null);
            CompactPillBorder.Width = targetW;
        }

        GamingCapsuleContent.Visibility = Visibility.Collapsed;
        PillNormalContent.Visibility = Visibility.Collapsed;
        CapsuleVolumeOverlay.Visibility = Visibility.Collapsed;
        CapsulePowerOverlay.Visibility = Visibility.Collapsed;
        CapsuleResolutionOverlay.Visibility = Visibility.Visible;

        _resolutionNoticeTimer?.Stop();
        _resolutionNoticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _resolutionNoticeTimer.Tick += (s, e) =>
        {
            _resolutionNoticeTimer?.Stop();
            CapsuleResolutionOverlay.Visibility = Visibility.Collapsed;
            UpdateCompactPillDimensions();
        };
        _resolutionNoticeTimer.Start();
    }

    private double _lastPowerNoticeWidth = 190.0;
    private System.Windows.Threading.DispatcherTimer? _powerNoticeTimer;

    private void OnSystemPowerChanged() => _viewModel.OnPowerState(MochiDock.Services.BatteryStatus.Read());

    // Silent charger / low-battery notice on the compact capsule; modelled on the resolution toast (own timer, natural width, 36-DIP height).
    private void ShowPowerNotice(MochiDock.Core.PowerNoticeKind kind, int percent)
    {
        if (_windowClosed || _viewModel.IsExpanded) return;

        string text = MochiDock.Core.PowerNoticePolicy.Text(kind, percent);
        bool warning = kind is MochiDock.Core.PowerNoticeKind.LowBattery or MochiDock.Core.PowerNoticeKind.CriticalBattery;
        CapsulePowerText.Text = text;
        CapsulePowerIcon.Data = Geometry.Parse(warning
            ? "M15.67,4H14V2h-4v2H8.33C7.6,4,7,4.6,7,5.33v15.33C7,21.4,7.6,22,8.33,22h7.33c0.74,0,1.34-0.6,1.34-1.33V5.33C17,4.6,16.4,4,15.67,4z M13,18h-2v-2h2V18z M13,14h-2V9h2V14z"
            : "M11,21h-1l1-7H7.5c-0.88,0-0.33-0.75-0.31-0.78C8.48,10.94,10.42,7.54,13.01,3h1l-1,7h3.51c0.4,0,0.62,0.19,0.4,0.66C12.97,17.55,11,21,11,21z");
        CapsulePowerIcon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            warning ? "IslandRedBrush" : kind == MochiDock.Core.PowerNoticeKind.PluggedIn ? "IslandGreenBrush" : "IslandInkBrush");

        var typeface = new System.Windows.Media.Typeface(CapsulePowerText.FontFamily, CapsulePowerText.FontStyle, CapsulePowerText.FontWeight, CapsulePowerText.FontStretch);
        var formatted = new System.Windows.Media.FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            typeface, CapsulePowerText.FontSize > 0 ? CapsulePowerText.FontSize : 11.5, System.Windows.Media.Brushes.Black, System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip);
        // Icon (14) + gap (8) + symmetrical pill padding (18 each side).
        _lastPowerNoticeWidth = Math.Max(160, Math.Ceiling(formatted.WidthIncludingTrailingWhitespace + 22 + 36));

        CapsulePowerOverlay.Visibility = Visibility.Visible;
        UpdateCompactPillDimensions();

        _powerNoticeTimer?.Stop();
        _powerNoticeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(MochiDock.Core.PowerNoticePolicy.DisplaySeconds(kind)) };
        _powerNoticeTimer.Tick += (s, e) =>
        {
            _powerNoticeTimer?.Stop();
            CapsulePowerOverlay.Visibility = Visibility.Collapsed;
            if (!_windowClosed) UpdateCompactPillDimensions();
        };
        _powerNoticeTimer.Start();
    }

    private void OnMasterVolumeChanged(float vol, bool muted)
    {
        if (_windowClosed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_windowClosed || _policyHidden || _viewModel.IsExpanded) return;
            _hoverExpandTimer.Stop();
            _isHoverNudged = false;
            _isAdjustingVolume = true;
            CompactPillBorder.BeginAnimation(HeightProperty, null);
            CompactPillBorder.Height = GetCurrentCompactTargetHeight();
            ShowCapsuleVolume(vol, muted);
        });
    }

    private void CompactPill_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        _hoverExpandTimer.Stop();
        _isHoverNudged = false;
        _isAdjustingVolume = true;
        CompactPillBorder.BeginAnimation(HeightProperty, null);
        CompactPillBorder.Height = GetCurrentCompactTargetHeight();

        float delta = e.Delta > 0 ? 2f : -2f;
        float newVol = _viewModel.AudioService.AdjustMasterVolume(delta);
        ShowCapsuleVolume(newVol, _viewModel.AudioService.IsMuted());
    }

    private void CompactPill_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            e.Handled = true;
            _hoverExpandTimer.Stop();
            _isHoverNudged = false;
            _isAdjustingVolume = true;
            CompactPillBorder.BeginAnimation(HeightProperty, null);
            CompactPillBorder.Height = GetCurrentCompactTargetHeight();

            bool isMuted = _viewModel.AudioService.ToggleMute();
            float vol = _viewModel.AudioService.GetMasterVolume();
            ShowCapsuleVolume(vol, isMuted);
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            _dragStartPoint = PointToScreen(e.GetPosition(this));
            _dragStartLeft = this.Left;
            _isDraggingDock = false;
            CompactPillBorder.CaptureMouse();
        }
    }

    private void CompactPill_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && CompactPillBorder.IsMouseCaptured)
        {
            var currentScreen = PointToScreen(e.GetPosition(this));
            var (dpiX, _) = MultiMonitorHelper.GetDpiScale(this);
            double deltaX = (currentScreen.X - _dragStartPoint.X) / dpiX;
            if (!_isDraggingDock && Math.Abs(deltaX) > MochiDock.Core.SpotPolicy.DragSlopDip)
            {
                _isDraggingDock = true;
                _hoverExpandTimer.Stop();
            }

            if (_isDraggingDock)
            {
                var targetScreen = MultiMonitorHelper.GetTargetScreen(_settings.TargetMonitor);
                if (targetScreen == null || targetScreen.WorkingArea.Width <= 0 || targetScreen.WorkingArea.Height <= 0) return;
                double screenLeftDip = targetScreen.WorkingArea.Left / dpiX;
                double screenWidthDip = targetScreen.WorkingArea.Width / dpiX;

                double targetLeft = _dragStartLeft + deltaX;
                double ratio = MochiDock.Core.SpotPolicy.CalculateRatio(screenLeftDip, screenWidthDip, this.Width, targetLeft);
                this.Left = MultiMonitorHelper.RoundToDevicePixel(MochiDock.Core.SpotPolicy.ComputeWindowLeftDip(screenLeftDip, screenWidthDip, this.Width, ratio), dpiX);
            }
        }
    }

    private void ShowCapsuleVolume(float volume, bool isMuted)
    {
        // Smoothly adjust compact pill width to 240 to avoid abrupt size snaps
        double currentW = CompactPillBorder.ActualWidth > 0 ? CompactPillBorder.ActualWidth : CompactPillBorder.Width;
        if (double.IsNaN(currentW) || currentW <= 0) currentW = 125;

        if (Math.Abs(currentW - 240) > 1)
        {
            var wAnim = new DoubleAnimation(currentW, 240, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CompactPillBorder.BeginAnimation(WidthProperty, wAnim);
        }
        else
        {
            CompactPillBorder.BeginAnimation(WidthProperty, null);
            CompactPillBorder.Width = 240;
        }

        GamingCapsuleContent.Visibility = Visibility.Collapsed;
        PillNormalContent.Visibility = Visibility.Collapsed;
        CapsuleVolumeOverlay.Visibility = Visibility.Visible;

        // Dynamic vector icons: Muted vs Low vs High
        if (isMuted || volume <= 0)
        {
            // Mute speaker path
            CapsuleVolumeVectorIcon.Data = Geometry.Parse("M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z");
            CapsuleVolumeVectorIcon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "IslandNegativeBrush");
        }
        else if (volume <= 50)
        {
            // Low / medium speaker path
            CapsuleVolumeVectorIcon.Data = Geometry.Parse("M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02z");
            CapsuleVolumeVectorIcon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "IslandVolumeBrush");
        }
        else
        {
            // Full speaker path
            CapsuleVolumeVectorIcon.Data = Geometry.Parse("M3,9v6h4l5,5V4L7,9H3z M16.5,12c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-0.73 2.5-2.25 2.5-4.02z M14,3.23v2.06c2.89,0.86 5,3.54 5,6.71s-2.11,5.85-5,6.71v2.06c4.01-0.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z");
            CapsuleVolumeVectorIcon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "IslandVolumeBrush");
        }

        // Smooth progress animation to glide cleanly rather than jump abruptly
        var anim = new DoubleAnimation(volume, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CapsuleVolumeProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim);
        CapsuleVolumeText.Text = isMuted ? "MUTE" : $"{volume:0}%";

        _volumeOverlayTimer.Stop();
        _volumeOverlayTimer.Start();
    }

    private void CapsuleMarquee_CycleCompleted(object sender, RoutedEventArgs e)
    {
        if (_isAdjustingVolume) return;

        // 1. If we are in media takeover mode, end takeover once minimum 4s has elapsed
        if (_viewModel.IsNotificationTakeover)
        {
            if ((DateTime.UtcNow - _takeoverStart).TotalSeconds >= 4.0)
            {
                _takeoverTimeoutTimer.Stop();
                _viewModel.EndNotificationTakeover();
            }
            return;
        }

        // 2. If notifications are rotating in the main pill (no media/stats), advance to the next notification
        if (_viewModel.IsNotificationInMainPill && _viewModel.NotificationCount > 1)
        {
            _viewModel.AdvanceNotificationRotation();
        }
    }

    private void CompactPill_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _viewModel.ReportUserActivity();
    }

    private void IslandBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel.IsHardwareNoticeVisible || _isAdjustingVolume) return;
        _viewModel.ReportUserActivity();
        if (_suppressHoverExpandUntilLeave) return;
        if ((DateTime.UtcNow - _lastManualCloseTime).TotalMilliseconds < 1200) return;

        _mouseLeaveTimer.Stop();
        _shrinkGraceTimer.Stop();

        if (!_viewModel.IsExpanded)
        {
            // Stage 1: Tactile "Nudge" swell (+14px width, +3px height)
            _isHoverNudged = true;
            double targetW = GetCurrentCompactTargetWidth() + 14;
            double targetH = GetCurrentCompactTargetHeight() + 3;
            var wAnim = new DoubleAnimation(targetW, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var hAnim = new DoubleAnimation(targetH, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            CompactPillBorder.BeginAnimation(WidthProperty, wAnim);
            CompactPillBorder.BeginAnimation(HeightProperty, hAnim);

            // Stage 2: Intentional Dwell Expansion after 320ms
            _hoverExpandTimer.Stop();
            _hoverExpandTimer.Start();
        }
    }

    private void IslandBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hoverExpandTimer.Stop();

        // If pill was nudged but not expanded, smoothly settle back down
        if (_isHoverNudged && !_viewModel.IsExpanded)
        {
            _isHoverNudged = false;
            double returnWidth = GetCurrentCompactTargetWidth();
            double returnHeight = GetCurrentCompactTargetHeight();
            var wAnim = new DoubleAnimation(returnWidth, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var hAnim = new DoubleAnimation(returnHeight, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            CompactPillBorder.BeginAnimation(WidthProperty, wAnim);
            CompactPillBorder.BeginAnimation(HeightProperty, hAnim);
        }

        if ((DateTime.UtcNow - _lastManualCloseTime).TotalMilliseconds >= 1200)
        {
            _suppressHoverExpandUntilLeave = false;
        }

        if (!_viewModel.IsPinned && _viewModel.IsExpanded)
        {
            _mouseLeaveTimer.Stop();
            _shrinkGraceTimer.Stop();
            if (MochiDock.Core.CollapseGrace.IsShrinkLeave(DateTime.UtcNow - _lastShrinkUtc) && TryCursorDip(out var cursor))
            {
                // The dock shrank out from under a still cursor: wait until the user moves away (or a few seconds pass).
                _shrinkLeaveUtc = DateTime.UtcNow;
                _shrinkLeaveCursor = cursor;
                _shrinkGraceTimer.Start();
            }
            else
            {
                _mouseLeaveTimer.Start();
            }
        }
    }

    private void ShrinkGraceTick()
    {
        if (_windowClosed || _viewModel.IsPinned || !_viewModel.IsExpanded) { _shrinkGraceTimer.Stop(); return; }
        if (ExpandedBorder.IsMouseOver || CompactContainer.IsMouseOver) { _shrinkGraceTimer.Stop(); return; }   // back inside: no close
        if (!TryCursorDip(out var cursor)) return;
        double moved = (cursor - _shrinkLeaveCursor).Length;
        double outside = DistanceFromExpanded(cursor);
        if (MochiDock.Core.CollapseGrace.Decide(DateTime.UtcNow - _shrinkLeaveUtc, moved, outside) != MochiDock.Core.CollapseGraceDecision.Collapse) return;
        _shrinkGraceTimer.Stop();
        // The usual close path (it still respects a context menu, a capture or a deferred collapse).
        _mouseLeaveTimer.Stop();
        _mouseLeaveTimer.Start();
    }

    // The cursor in this window's DIP coordinates (screen pixels converted with the window's own DPI).
    private bool TryCursorDip(out System.Windows.Point cursor)
    {
        cursor = default;
        if (!GetCursorPos(out var p) || PresentationSource.FromVisual(this) == null) return false;
        cursor = PointFromScreen(new System.Windows.Point(p.X, p.Y));
        return true;
    }

    // How far the cursor (window DIP) is outside the expanded dock; 0 when it is over it.
    private double DistanceFromExpanded(System.Windows.Point cursor)
    {
        var bounds = ExpandedBorder.TransformToAncestor(this).TransformBounds(new Rect(ExpandedBorder.RenderSize));
        double dx = Math.Max(0, Math.Max(bounds.Left - cursor.X, cursor.X - bounds.Right));
        double dy = Math.Max(0, Math.Max(bounds.Top - cursor.Y, cursor.Y - bounds.Bottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void CompactView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (CompactPillBorder.IsMouseCaptured)
        {
            CompactPillBorder.ReleaseMouseCapture();
        }

        if (_isDraggingDock)
        {
            _isDraggingDock = false;
            var targetScreen = MultiMonitorHelper.GetTargetScreen(_settings.TargetMonitor);
        if (targetScreen == null || targetScreen.WorkingArea.Width <= 0 || targetScreen.WorkingArea.Height <= 0) return;
            var (dpiX, _) = MultiMonitorHelper.GetDpiScale(this);
            double screenLeftDip = targetScreen.WorkingArea.Left / dpiX;
            double screenWidthDip = targetScreen.WorkingArea.Width / dpiX;

            double currentRatio = MochiDock.Core.SpotPolicy.CalculateRatio(screenLeftDip, screenWidthDip, this.Width, this.Left);
            _viewModel.IslandHorizontalRatio = MochiDock.Core.SpotPolicy.RememberRatio(currentRatio);
            e.Handled = true;
            return;
        }

        if (_isAdjustingVolume) return;
        _hoverExpandTimer.Stop();
        _isHoverNudged = false;
        CompactPillBorder.BeginAnimation(HeightProperty, null);
        CompactPillBorder.Height = GetCurrentCompactTargetHeight();
        if (_viewModel.IsHardwareNoticeVisible) { _viewModel.OpenHardwareNotice(); return; }
        if (!_viewModel.IsExpanded) _viewModel.PrepareHoverExpand();
        _viewModel.IsExpanded = true;
    }

    private void CompanionBubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.OpenCompanionActivity();
    }

    private void TabMedia_Click(object sender, RoutedEventArgs e) { _viewModel.ClearTransientCompanionView(); _viewModel.ShowMedia(); }
    private void TabWhatsApp_Click(object sender, RoutedEventArgs e) { _viewModel.ClearTransientCompanionView(); _viewModel.ShowWhatsApp(); }
    private void TabDiscord_Click(object sender, RoutedEventArgs e) { _viewModel.ClearTransientCompanionView(); _viewModel.ShowDiscord(); }
    private void TabApps_Click(object sender, RoutedEventArgs e) { _viewModel.ClearTransientCompanionView(); _viewModel.ShowApps(); }
    private void TabEmail_Click(object sender, RoutedEventArgs e) { _viewModel.ClearTransientCompanionView(); _viewModel.ShowEmail(); }
    private void TabHardware_Click(object sender, RoutedEventArgs e) { _viewModel.ClearTransientCompanionView(); _viewModel.ShowHardware(); }
    private void TabWeather_Click(object sender, RoutedEventArgs e) { _viewModel.ClearTransientCompanionView(); _viewModel.ShowWeather(); }
    private void TabPomodoro_Click(object sender, RoutedEventArgs e) { _viewModel.ClearTransientCompanionView(); _viewModel.ShowPomodoro(); }
    private void TabPrayer_Click(object sender, RoutedEventArgs e) { _viewModel.ClearTransientCompanionView(); _viewModel.ShowPrayer(); }
    private void TabFileShelf_Click(object sender, RoutedEventArgs e) { _viewModel.ClearTransientCompanionView(); _viewModel.ShowFileShelf(); }
    private void TabClipboard_Click(object sender, RoutedEventArgs e) { _viewModel.ClearTransientCompanionView(); _viewModel.ShowClipboard(); }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearTransientCompanionView();
        _viewModel.ShowSettings();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsPinned = !_viewModel.IsPinned;
        if (_viewModel.IsPinned)
        {
            _viewModel.ClearTransientCompanionView();
            _mouseLeaveTimer.Stop();
        }
    }



    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_viewModel.IsExpanded) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase { IsReadOnly: false }) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.ComboBox { IsDropDownOpen: true }) return;
        _viewModel.IsPinned = false;
        _viewModel.IsExpanded = false;
        e.Handled = true;
    }

    private void WelcomeSetCity_Click(object sender, RoutedEventArgs e)
    {
        App.OpenSettings(MochiDock.Core.SettingsSections.Location);
        e.Handled = true;
    }

    private void WelcomeTabs_Click(object sender, RoutedEventArgs e)
    {
        App.OpenSettings(MochiDock.Core.SettingsSections.Tabs);
        e.Handled = true;
    }

    private void WelcomeDismiss_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DismissFirstRunWelcome();
        e.Handled = true;
    }

    private async void UpdateBannerAction_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        try { await _viewModel.UpdateBannerActionAsync(); }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("Update", ex); }
    }

    private void UpdateBannerNotes_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenUpdatePage();
        e.Handled = true;
    }

    private void UpdateBannerDismiss_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DismissUpdateNudge();
        e.Handled = true;
    }

    private void UpdateBannerCancel_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelUpdateDownload();
        e.Handled = true;
    }

    private bool _policyHidden;
    private MochiDock.Services.PowerNotifications? _powerNotifications;
    private readonly System.Windows.Threading.DispatcherTimer _policyTimer = new(){Interval=TimeSpan.FromMilliseconds(500)};
    private void PolicyTick(object? sender,EventArgs e) {
        if(_viewModel.IsAutoCollapseDeferred&&!_viewModel.IsPinned&&!(_viewModel.IsDockInteractionActive?.Invoke()??false)){_viewModel.IsAutoCollapseDeferred=false;_mouseLeaveTimer.Stop();_mouseLeaveTimer.Start();}
        bool hide=MochiDock.Core.LocalFeaturePolicy.Hide(_viewModel.HideInFullscreen,MochiDock.Services.FullscreenPolicy.IsFullscreen(new WindowInteropHelper(this).Handle,_settings.TargetMonitor),_viewModel.IsGamingSessionActive||_viewModel.IsStatsPinnedToCapsule);
        if(hide&&!_policyHidden){_viewModel.SetHardwarePresentationSuppressed(true);_policyHidden=true;Hide();}
        else if(!hide&&_policyHidden){_viewModel.SetHardwarePresentationSuppressed(false);_policyHidden=false;Show();EnsureWindowTopmost();}
        if(!_policyHidden&&_viewModel.IsGamingSessionActive)EnsureWindowTopmost();
    }
    private void HardwareNotice_Click(object sender,RoutedEventArgs e){_viewModel.OpenHardwareNotice();e.Handled=true;}
    private void PrayerReminderOK_Click(object sender,RoutedEventArgs e){_viewModel.AcknowledgePrayerCountdown();e.Handled=true;}
    private void PrayerArrivalOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.DismissPrayerArrival();
        _viewModel.ClearTransientCompanionView();
        _viewModel.ShowPrayer();
        if (!_viewModel.IsExpanded)
        {
            _viewModel.IsExpanded = true;
        }
    }
    private void TabToday_Click(object sender,RoutedEventArgs e){_viewModel.NavigateManually(IslandCardType.Today);}
    private void TabAiUsage_Click(object sender,RoutedEventArgs e){_viewModel.ClearTransientCompanionView();_viewModel.ShowAiUsage();}
    private void TabScript_Click(object sender,RoutedEventArgs e){_viewModel.ClearTransientCompanionView();_viewModel.ShowScript();}
    private void TabDownloads_Click(object sender,RoutedEventArgs e){_viewModel.ClearTransientCompanionView();_viewModel.ShowDownloads();}
    private void TabFxRates_Click(object sender,RoutedEventArgs e){_viewModel.ClearTransientCompanionView();_viewModel.NavigateManually(IslandCardType.FxRates);}
    private void HeaderTabs_Wheel(object sender,MouseWheelEventArgs e) {
        var tabs=HeaderTabsPanel.Children.OfType<System.Windows.Controls.Button>().Where(b=>b.IsVisible).ToList();
        if(tabs.Count==0)return;
        var active=tabs.FindIndex(b=>Equals(b.Tag,"Active"));
        var next=MochiDock.Core.LocalFeaturePolicy.NextTab(active,tabs.Count,e.Delta<0);
        tabs[next].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));e.Handled=true;
    }
    private bool _headerRevealPending;
    private void QueueHeaderSelectionReveal()
    {
        if (_windowClosed || _headerRevealPending || HeaderTabsPanel == null) return;
        _headerRevealPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            _headerRevealPending = false;
            if (_windowClosed) return;
            HeaderTabsPanel.UpdateLayout();
            AnimateHeaderSelection();
            HeaderTabsPanel.Children.OfType<System.Windows.Controls.Button>()
                .FirstOrDefault(b => b.Visibility == Visibility.Visible && Equals(b.Tag, "Active"))?.BringIntoView();
        });
    }
    protected override void OnDpiChanged(System.Windows.DpiScale oldDpi,System.Windows.DpiScale newDpi){
        base.OnDpiChanged(oldDpi,newDpi);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,()=>{if(_windowClosed)return;PositionWindow();UpdateCompactPillWidth();HeaderTabsPanel.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault(b=>Equals(b.Tag,"Active"))?.BringIntoView();});
    }
    private void AnimateHeaderSelection() => HeaderPillOverlay.Refresh(SystemParameters.ClientAreaAnimation);

    private string? _cachedHeaderDateText;
    private double _cachedHeaderDateWidth;

    private void HeaderTabsPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Only update selection highlight if size change wasn't just a sub-pixel shift
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5)
        {
            AnimateHeaderSelection();
        }

        // Measure the date's hidden footprint too, so the visibility decision is stable.
        if (HeaderDateText == null || HeaderTabsViewport == null || HeaderTabsPanel == null) return;

        string curText = HeaderDateText.Text ?? string.Empty;
        if (_cachedHeaderDateText != curText || _cachedHeaderDateWidth <= 0)
        {
            var probe = new TextBlock { Text = curText, FontFamily = HeaderDateText.FontFamily,
                FontSize = HeaderDateText.FontSize, FontWeight = HeaderDateText.FontWeight };
            probe.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            _cachedHeaderDateWidth = probe.DesiredSize.Width + HeaderDateText.Margin.Left + HeaderDateText.Margin.Right;
            _cachedHeaderDateText = curText;
        }

        double availableWithoutDate = HeaderTabsViewport.ActualWidth +
            (HeaderDateText.Visibility == Visibility.Visible ? _cachedHeaderDateWidth : 0);
        HeaderDateText.Visibility = HeaderTabsPanel.DesiredSize.Width + _cachedHeaderDateWidth <= availableWithoutDate
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
