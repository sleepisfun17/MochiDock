using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using MochiDock.Controls;
using MochiDock.Models;
using MochiDock.Services;
using MascotMood = MochiDock.Core.MascotMood;

namespace MochiDock.ViewModels;

public enum IslandCardType
{
    Media,
    Email,
    WhatsApp,
    Discord,
    Apps,
    Settings,
    Hardware,
    Summary,
    Weather,
    Pomodoro,
    Prayer,
    FileShelf,
    Clipboard,
    Today,
    FxRates,
    AiUsage,
    Script,
    Downloads
}

public partial class MainViewModel : INotifyPropertyChanged
{
    // ───────────── Resting clock and header date/time ─────────────
    private DateTime _lastCalendarDate = DateTime.Today;
    public string CurrentTimeText => DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    public string CurrentDateText => MochiDock.Core.DateDisplay.Full(DateTime.Now);
    public string HeaderDateTimeText => $"{CurrentDateText}  {CurrentTimeText}";
    public string CurrentDayOfWeekUpper => DateTime.Now.ToString("dddd", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();
    public string CurrentDateMonthText => DateTime.Now.ToString("MM/dd", System.Globalization.CultureInfo.InvariantCulture);
    public ObservableCollection<CalendarDayItem> CalendarDays { get; private set; } = CalendarHelper.GenerateMonthDays(DateTime.Today);

    // ───────────── Alive Idle Thoughts & Personality (Typewriter Engine) ─────────────
    private readonly string[] _idleThoughts = new[]
    {
        "waiting for you...",
        "boring...",
        "poking around...",
        "humming softly 🎵",
        "daydreaming...",
        "stretching...",
        "listening to the breeze...",
        "peek-a-boo~"
    };
    private int _idleThoughtIndex = 0;
    private readonly DispatcherTimer _idleTypewriterTimer;
    private string _targetIdleThought = "waiting for you...";
    private string _currentIdleThought = "waiting for you...";
        // ───────────── Alive Inactivity Sleep Tracker ─────────────
    private DateTime _lastActivityTime = DateTime.Now;
    private readonly DispatcherTimer _inactivitySleepTimer;

    public void ReportUserActivity()
    {
        _lastActivityTime = DateTime.Now;
        if (_baseMood is MascotMood.Sleeping or MascotMood.Coffee or MascotMood.Bored && !_isWorkstationLocked && _displayAwake)
        {
            MascotMood = MascotMood.Idle;   // you came to the dock: Mochi looks up
        }
    }
    private enum TypewriterState { Typing, Pausing, Deleting }
    private TypewriterState _typewriterState = TypewriterState.Pausing;
    private int _pauseTicks = 50; // ~5 seconds pause at 100ms ticks

    public string CurrentIdleThought
    {
        get => _currentIdleThought;
        set
        {
            if (_currentIdleThought != value)
            {
                _currentIdleThought = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CompactCapsuleText));
                OnPropertyChanged(nameof(WeatherPillText));
            }
        }
    }

    private void OnTypewriterTick()
    {
        if (HasActiveMedia || IsStatsShownInCapsule || IsNotificationInMainPill || IsPomodoroInMainPill)
            return;

        switch (_typewriterState)
        {
            case TypewriterState.Pausing:
                _pauseTicks--;
                if (_pauseTicks <= 0)
                {
                    _typewriterState = TypewriterState.Deleting;
                }
                break;

            case TypewriterState.Deleting:
                if (!string.IsNullOrEmpty(_currentIdleThought))
                {
                    // Backspace one character (accounting for emoji/surrogates)
                    if (_currentIdleThought.Length >= 2 && char.IsSurrogatePair(_currentIdleThought, _currentIdleThought.Length - 2))
                    {
                        CurrentIdleThought = _currentIdleThought.Substring(0, _currentIdleThought.Length - 2);
                    }
                    else
                    {
                        CurrentIdleThought = _currentIdleThought.Substring(0, _currentIdleThought.Length - 1);
                    }
                }
                else
                {
                    // Advance to next thought
                    _idleThoughtIndex = (_idleThoughtIndex + 1) % _idleThoughts.Length;
                    _targetIdleThought = _idleThoughts[_idleThoughtIndex];
                    _typewriterState = TypewriterState.Typing;

                    if (_baseMood == MascotMood.Idle || _baseMood == MascotMood.Waiting)
                    {
                        MascotMood = (_idleThoughtIndex % 2 == 0) ? MascotMood.Waiting : MascotMood.Idle;
                    }
                }
                break;

            case TypewriterState.Typing:
                if (_currentIdleThought.Length < _targetIdleThought.Length)
                {
                    int nextLen = _currentIdleThought.Length + 1;
                    if (nextLen < _targetIdleThought.Length && char.IsHighSurrogate(_targetIdleThought[nextLen - 1]))
                    {
                        nextLen++;
                    }
                    CurrentIdleThought = _targetIdleThought.Substring(0, nextLen);
                }
                else
                {
                    _typewriterState = TypewriterState.Pausing;
                    _pauseTicks = 45; // ~4.5 seconds pause after completing word
                }
                break;
        }
    }

    private readonly MediaService _mediaService;
    private readonly IMailService _mailService;
    private readonly GowaService _gowaService;
    private readonly DiscordNotificationService _discordService;
    private readonly WeatherService _weatherService;
    private readonly PomodoroService _pomodoroService;
    private readonly PrayerTimesService _prayerTimesService;
    private readonly LrclibService _lrclibService;
    private readonly TasksService _tasksService;
    private readonly FileShelfService _fileShelfService;
    private readonly ClipboardService _clipboardService;
    private readonly AiUsageService _aiUsageService;
    public AiUsageService AiUsageService => _aiUsageService;
    public FileShelfService FileShelfService => _fileShelfService;
    private WeatherInfo? _weatherInfo;
    private readonly DispatcherTimer _weatherPollTimer;
    private bool _isWeatherLoading;
    public bool IsWeatherLoading
    {
        get => _isWeatherLoading;
        set { _isWeatherLoading = value; OnPropertyChanged(); }
    }

    public WeatherInfo? WeatherInfo
    {
        get => _weatherInfo;
        set
        {
            _weatherInfo = value;
            _mascotWeather = _mascotWeatherTracker.Update(value?.Temperature);
            RefreshMascotMood();
            OnPropertyChanged();
            OnPropertyChanged(nameof(WeatherDisplayText));
            OnPropertyChanged(nameof(WeatherConditionText));
            OnPropertyChanged(nameof(WeatherIconText));
            OnPropertyChanged(nameof(WeatherTemperatureBadge));
            OnPropertyChanged(nameof(WeatherTemperatureShort));
            RaiseCapsuleStateChanged();
        }
    }
    public string WeatherCityDisplay => !string.IsNullOrWhiteSpace(_settings?.WeatherCity) ? _settings.WeatherCity : "Tokyo";
        public string WeatherTemperatureBadge => _weatherInfo != null ? $"{_weatherInfo.Temperature:0}°C" : "";
    public string WeatherTemperatureShort => _weatherInfo != null ? $"{_weatherInfo.Temperature:0}°" : "";
    public string WeatherDisplayText => _weatherInfo != null
        ? $"{_weatherInfo.Temperature:0}°C · {_weatherInfo.WindSpeed:0.0} m/s"
        : (_isWeatherLoading ? "Loading weather..." : "Weather unavailable");
    public string WeatherConditionText => _weatherInfo?.Condition ?? (_isWeatherLoading ? "Updating forecast..." : "Check connection or city");
    public string WeatherIconText => _weatherInfo?.WeatherIcon ?? "☀️";
    public bool IsWeatherInMainPill =>
        (_settings?.ShowWeatherInCapsule ?? false) && _weatherInfo != null &&
        !HasActiveMedia && !IsNotificationInMainPill && !IsPrayerReminderVisible && !IsPrayerArrivalVisible;
    public bool IsWeatherInPrayerReminder => (_settings?.ShowWeatherInCapsule ?? false) && _weatherInfo != null && IsPrayerReminderVisible;
    public string WeatherPillText => CurrentTimeText;
    /// <summary>The capsule shows only the clock (no weather, Pomodoro, media or notice). The side columns match so the time is centred.</summary>
    public bool IsRestingClockPill => !IsWeatherInMainPill && !string.IsNullOrEmpty(CompactCapsuleText) && CompactCapsuleText == CurrentTimeText;
    public bool IsPrimaryCapsuleReading => !HasActiveMedia && !IsNotificationInMainPill && !IsHardwareNoticeVisible && !IsPrayerArrivalVisible;

    private readonly HardwareMonitorService _hardwareService = new();
    private readonly AudioEndpointService _audioService = new();
    public AudioEndpointService AudioService => _audioService;
    private readonly DispatcherTimer _autoCollapseTimer;
    private readonly DispatcherTimer _hardwarePollTimer;
    private bool _hardwarePollInFlight;

    private AppSettings _settings;
    public AppSettings Settings => _settings;

    private bool _isExpanded;
    private bool _isPinned;
    private IslandCardType _activeCardType = IslandCardType.Media;
    private MascotMood _mascotMood = MascotMood.Idle;
    // The mood the rest of the app asks for (set through MascotMood); the face shown is the policy's choice of it plus the ambient inputs.
    private MascotMood _baseMood = MascotMood.Idle;
    private bool _mascotOverdue, _mascotGpuHot, _mascotOverloaded, _mascotBatteryLow;
    private MochiDock.Core.MascotWeather _mascotWeather = MochiDock.Core.MascotWeather.Mild;
    private readonly MochiDock.Core.MascotWeatherTracker _mascotWeatherTracker = new();
    private readonly MochiDock.Core.SustainedLoadTracker _mascotLoadTracker = new();

    private HardwareStats _hardwareStats = new();
    private string _currentAudioDeviceName = "Audio Output";
    private System.Windows.Media.Color _ambientGlowColor = System.Windows.Media.Color.FromArgb(140, 10, 132, 255);

    // Gaming DND & Lock Screen State
    private bool _wasGaming;
    private int _queuedGamingWhatsAppCount;
    private int _queuedGamingEmailCount;
    private int _queuedGamingDiscordCount;
    private readonly List<string> _queuedGamingPreviews = new();

    private bool _isWorkstationLocked;
    private DateTime _snoozedUntil = DateTime.MinValue;

    private string _capsuleTheme = "Default";
    private double _capsuleScrollSpeed = 35;
    public string CapsuleTheme { get => _capsuleTheme; private set { _capsuleTheme = value; OnPropertyChanged(); } }
    public double CapsuleScrollSpeed { get => _capsuleScrollSpeed; private set { _capsuleScrollSpeed = value; OnPropertyChanged(); } }
    

    
    private string _idleStatusText = "Mochi is sleeping...";
    public string IdleStatusText
    {
        get => _idleStatusText;
        set { _idleStatusText = value; OnPropertyChanged(); OnPropertyChanged(nameof(CompactCapsuleText)); }
    }
    /// <summary>True when more than one WhatsApp account is linked; account labels are shown only then.</summary>
    public bool HasMultipleWhatsAppAccounts => MochiDock.Core.GowaAccountPolicy.ShowLabels(_gowaService.Accounts.Count);

    /// <summary>True when WhatsApp is on and at least one device is linked.</summary>
    public bool WhatsAppIsLinked => _gowaService.Enabled && _gowaService.Accounts.Count > 0;

    /// <summary>One line for the WhatsApp settings page: off, not linked yet (with the reason when known) or the linked device.</summary>
    public string WhatsAppLinkStatus
    {
        get
        {
            if (!_gowaService.Enabled) return "WhatsApp is off.";
            if (_gowaService.Accounts.Count > 0)
                return !string.IsNullOrEmpty(_gowaService.ConnectedDeviceName) ? $"Linked: {_gowaService.ConnectedDeviceName}" : "Linked.";
            return string.IsNullOrEmpty(_gowaService.ConnectionProblem) ? "Not linked yet." : $"Not linked yet ({_gowaService.ConnectionProblem}).";
        }
    }

    public string IntegrationStatus
    {
        get
        {
            string modeLabel = _gowaService.UseRemoteServer ? "Server" : "Local";
            string waStatus = !_gowaService.Enabled ? "WhatsApp: Off" : _gowaService.IsHealthy 
                ? (!string.IsNullOrEmpty(_gowaService.ConnectedDeviceName) ? $"WhatsApp ({modeLabel}): {_gowaService.ConnectedDeviceName}" : $"WhatsApp ({modeLabel}): Connected") 
                : $"WhatsApp ({modeLabel}): Disconnected" + (string.IsNullOrEmpty(_gowaService.ConnectionProblem) ? "" : $" ({_gowaService.ConnectionProblem})");
            string mailStatus = _mailService.IsAvailable ? "Mail: Ready" : "Mail: Unavailable";
            return $"{waStatus} · {mailStatus}";
        }
    }

    public async Task RefreshIntegrationStatusAsync()
    {
        await _gowaService.CheckConnectionAsync();
        OnPropertyChanged(nameof(IntegrationStatus));
    }

    /// <summary>Re-reads settings and restarts WhatsApp, mail and weather. Completes when the connection check is done (the buttons show it as busy).</summary>
    public async Task RetryIntegrations()
    {
        _settings = SettingsService.Load();
        _gowaService.ConfigureFromSettings(_settings);
        ImapMail?.Configure(_settings.ImapAccounts);
        _mailService.Stop();
        _mailService.Start();
        await _gowaService.StartAsync();
        _ = PollWeatherAsync();
        await Task.Delay(1200);
        await _gowaService.CheckConnectionAsync();
        OnPropertyChanged(nameof(IntegrationStatus));
    }

    private string _currentWeatherCity = "";

    public async Task ApplyImportedSettings(AppSettings settings)
    {
        bool gowaChanged = _gowaService.Enabled != settings.EnableWhatsApp ||
                           _gowaService.UseRemoteServer != string.Equals(settings.GowaMode, "Remote", StringComparison.OrdinalIgnoreCase) ||
                           !string.Equals(_gowaService.RemoteServerUrl, settings.RemoteGowaUrl, StringComparison.OrdinalIgnoreCase) ||
                           _gowaService.GowaPort != settings.GowaPort ||
                           _gowaService.WebhookPort != settings.WebhookPort ||
                           _gowaService.RemoteCredentialsChanged;

        string targetCity = string.IsNullOrWhiteSpace(settings.WeatherCity) ? "Tokyo" : settings.WeatherCity.Trim();
        bool cityChanged = !string.Equals(_currentWeatherCity, targetCity, StringComparison.OrdinalIgnoreCase);

        _settings = settings;
        SyncWatchedApps();
        ImapMail?.Configure(_settings.ImapAccounts);
        _aiUsageService.HiddenProviders = _settings.HiddenAiProviders ??= new();
        _aiUsageService.AlertsEnabled = _settings.EnableAiUsageAlerts;
        _settings.LastActiveCardType = ValidManualCard();
        _settings.ActiveSystemModule = MochiDock.Core.NavigationPolicy.Module(_settings.ActiveSystemModule);
        NotifySystemModuleSelection();
        PrayerAudio.Stop();
        _snoozedUntil = settings.SnoozeUntilTicks > DateTime.Now.Ticks ? new DateTime(settings.SnoozeUntilTicks) : DateTime.MinValue;
        ApplyCapsuleTheme(settings.CapsuleTheme);
        SetCapsuleScrollSpeed(settings.CapsuleScrollSpeed);
        OnPropertyChanged(nameof(CompactCapsuleText));
        OnPropertyChanged(nameof(WeatherCityDisplay));
        RaiseCapsuleStateChanged();

        if (cityChanged || _weatherInfo == null)
        {
            _ = PollWeatherAsync();
            _ = UpdatePrayerLocationAndScheduleAsync();
        }
        else
        {
            _ = UpdatePrayerScheduleAsync();
        }
        ApplyDateFormat();
        OnPropertyChanged(nameof(Settings));
        // OnboardingDismissed comes from the imported file: the welcome banner and the dock height read it.
        OnPropertyChanged(nameof(IsFirstRunWelcomeVisible));
        SettingsChanged?.Invoke(SettingsApply.Hotkey);
        ApplyReminderSettings();
        OnPropertyChanged(nameof(ShowStatsTab));
        OnPropertyChanged(nameof(ShowWeatherTab));
        OnPropertyChanged(nameof(ShowPomodoroTab));
        OnPropertyChanged(nameof(EnablePrayerTimes));
        OnPropertyChanged(nameof(ShowTodayTab));OnPropertyChanged(nameof(ShowFxRatesTab));
        OnPropertyChanged(nameof(ShowAiUsageTab));
        OnPropertyChanged(nameof(ShowScriptTab));
        OnPropertyChanged(nameof(ShowDownloadsTab));
        Fx?.Configure(ShowFxRatesTab, _isWorkstationLocked || !_displayAwake);
        Fx?.SetPairs(_settings.FxPairs);   // imported or reloaded settings bring their own pairs (no-op when unchanged)
        OnPropertyChanged(nameof(FxPairs));
        OnPropertyChanged(nameof(EnableDeviceBatteries));OnPropertyChanged(nameof(ShowDeviceBatteriesOnTaskbar));OnPropertyChanged(nameof(ShowDeviceBatteryBackground));
        ApplyDeviceBatteryConfiguration();DeviceBatterySettingsChanged?.Invoke();
        OnPropertyChanged(nameof(TaskbarShowCpuLoad));OnPropertyChanged(nameof(TaskbarShowCpuTemp));OnPropertyChanged(nameof(TaskbarShowGpuLoad));OnPropertyChanged(nameof(TaskbarShowGpuTemp));
        OnPropertyChanged(nameof(TaskbarShowMemory));OnPropertyChanged(nameof(TaskbarShowDisk));OnPropertyChanged(nameof(TaskbarShowUpload));OnPropertyChanged(nameof(TaskbarShowDownload));
        OnPropertyChanged(nameof(TaskbarShowPrayer));OnPropertyChanged(nameof(TaskbarShowWeatherTemp));OnPropertyChanged(nameof(TaskbarShowWeatherWind));
        OnPropertyChanged(nameof(TaskbarBlockOrder));OnPropertyChanged(nameof(TaskbarMonitor));OnPropertyChanged(nameof(TaskbarPlacement));
        OnPropertyChanged(nameof(TaskbarOffsetPx));OnPropertyChanged(nameof(TaskbarLeftOffsetPx));OnPropertyChanged(nameof(TaskbarVerticalOffsetPx));
        OnPropertyChanged(nameof(TaskbarFontFamily));OnPropertyChanged(nameof(TaskbarFontSize));OnPropertyChanged(nameof(TaskbarFontBold));
        OnPropertyChanged(nameof(TaskbarBlockSpacing));OnPropertyChanged(nameof(TaskbarStableWidth));
        OnPropertyChanged(nameof(TaskbarLabelColor));OnPropertyChanged(nameof(TaskbarValueColor));
        OnPropertyChanged(nameof(TaskbarNetworkArrows));OnPropertyChanged(nameof(TaskbarNetworkUnit));
        OnPropertyChanged(nameof(TaskbarSwapNetworkLines));OnPropertyChanged(nameof(TaskbarCompactLabels));OnPropertyChanged(nameof(TaskbarRefreshSeconds));OnPropertyChanged(nameof(TaskbarBatteryLayout));
        ApplyTaskbarRefreshInterval();ApplyTaskbarSampler();RefreshTaskbarBlocks();TaskbarStyleChanged?.Invoke();TaskbarStyleChanged?.Invoke();
        OnPropertyChanged(nameof(EnablePowerLogging));_powerComponentItems=null;OnPropertyChanged(nameof(PowerComponentItems));RecomputePowerAllowance();ApplyPowerLogConfiguration();
        OnPropertyChanged(nameof(HideInFullscreen));
        OnPropertyChanged(nameof(EnableHardwareAlerts));
        OnPropertyChanged(nameof(ShowFileShelfTab));
        OnPropertyChanged(nameof(ShowClipboardTab));
        OnPropertyChanged(nameof(ShowLyricsPronunciation));
        OnPropertyChanged(nameof(ShowLyricsFurigana));
        OnPropertyChanged(nameof(EnableSyncedLyrics));OnPropertyChanged(nameof(EnableSounds));OnPropertyChanged(nameof(EnablePowerAlerts));
        OnPropertyChanged(nameof(LyricsOffsetMs));OnPropertyChanged(nameof(LyricsOffsetText));
        SoundService.Enabled = _settings.EnableSounds;
        OnPropertyChanged(nameof(IsPrayerInMainPill));

        if ((ActiveCardType == IslandCardType.Hardware && !ShowStatsTab) ||
            (ActiveCardType == IslandCardType.Weather && !ShowWeatherTab) ||
            (ActiveCardType == IslandCardType.Pomodoro && !ShowPomodoroTab) ||
            (ActiveCardType == IslandCardType.Prayer && !EnablePrayerTimes) ||
            (ActiveCardType == IslandCardType.Today && !ShowTodayTab) ||
            (ActiveCardType == IslandCardType.FxRates && !ShowFxRatesTab) ||
            (ActiveCardType == IslandCardType.AiUsage && !ShowAiUsageTab) ||
            (ActiveCardType == IslandCardType.Script && !ShowScriptTab) ||
            (ActiveCardType == IslandCardType.Downloads && !ShowDownloadsTab) ||
            (ActiveCardType == IslandCardType.FileShelf && !ShowFileShelfTab) ||
            (ActiveCardType == IslandCardType.Clipboard && !ShowClipboardTab))
        {
            ActiveCardType = IslandCardType.Media;
        }

        if (gowaChanged)
        {
            _gowaService.ConfigureFromSettings(_settings);
            await _gowaService.StartAsync();
            await Task.Delay(1200);
        }
        else
        {
            _gowaService.ConfigureFromSettings(_settings);
        }

        await _gowaService.CheckConnectionAsync();
        OnPropertyChanged(nameof(IntegrationStatus));
    }
    private DateTime _lockTimestamp = DateTime.MinValue;
    private int _lockedWhatsAppCount;
    private int _lockedEmailCount;
    private int _lockedDiscordCount;
    private readonly List<string> _lockedPreviews = new();

    // Summary Card properties
    private string _summaryTitle = "Session Summary";
    private string _summarySubtitle = "While you were away:";
    private int _summaryWhatsAppCount;
    private int _summaryEmailCount;
    private string _summaryDetails = "";
    private bool _summaryIsMorning;

    public string SummaryTitle
    {
        get => _summaryTitle;
        set { _summaryTitle = value; OnPropertyChanged(); }
    }

    public string SummarySubtitle
    {
        get => _summarySubtitle;
        set { _summarySubtitle = value; OnPropertyChanged(); }
    }

    public int SummaryWhatsAppCount
    {
        get => _summaryWhatsAppCount;
        set { _summaryWhatsAppCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSummaryWhatsApp)); }
    }

    public int SummaryEmailCount
    {
        get => _summaryEmailCount;
        set { _summaryEmailCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSummaryEmail)); }
    }

    public bool HasSummaryWhatsApp => _summaryWhatsAppCount > 0;
    public bool HasSummaryEmail => _summaryEmailCount > 0;

    public string SummaryDetails
    {
        get => _summaryDetails;
        set { _summaryDetails = value; OnPropertyChanged(); }
    }

    public bool SummaryIsMorning
    {
        get => _summaryIsMorning;
        set { _summaryIsMorning = value; OnPropertyChanged(); }
    }

    public System.Windows.Media.Color AmbientGlowColor
    {
        get => _ambientGlowColor;
        set
        {
            if (_ambientGlowColor != value)
            {
                _ambientGlowColor = value;
                OnPropertyChanged();
            }
        }
    }

    private string _lastClockMinute = "";
    public event Action? MediaPositionUpdated;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(MediaService mediaService, IMailService mailService, GowaService gowaService, DiscordNotificationService discordService, WeatherService weatherService, PomodoroService pomodoroService, PrayerTimesService prayerTimesService, LrclibService lrclibService, TasksService tasksService, FileShelfService fileShelfService, ClipboardService clipboardService, Action<AppSettings>? saveNavigationSettings = null, AppSettings? initialSettings = null)
    {
        _saveNavigationSettings = saveNavigationSettings ?? SettingsService.Save;
        _lastClockMinute = CurrentTimeText;
        var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clockTimer.Tick += (s, e) =>
        {
            string nowMinute = CurrentTimeText;
            if (!string.Equals(_lastClockMinute, nowMinute, StringComparison.Ordinal))
            {
                _lastClockMinute = nowMinute;
                OnPropertyChanged(nameof(CurrentTimeText));
                OnPropertyChanged(nameof(CurrentDateText));
                OnPropertyChanged(nameof(HeaderDateTimeText));
                if (!_isExpanded && !HasActiveMedia && !IsStatsShownInCapsule && !IsNotificationInMainPill && !IsPomodoroInMainPill)
                {
                    OnPropertyChanged(nameof(CompactCapsuleText));
                }
            }
            UpdateActivityFace();   // Pomodoro state, FX change and the weather can change without navigation
            UpdateDayFace();        // throttled to every 30 seconds
            if (DateTime.Today != _lastCalendarDate)
            {
                _lastCalendarDate = DateTime.Today;
                CalendarDays = CalendarHelper.GenerateMonthDays(DateTime.Today);
                OnPropertyChanged(nameof(CalendarDays));
                OnPropertyChanged(nameof(CurrentDayOfWeekUpper));
                OnPropertyChanged(nameof(CurrentDateMonthText));
            }
            if (_currentPrayerSchedule != null)
            {
                UpdatePrayerSchedule();
                if (_activeCardType == IslandCardType.Prayer)
                {
                    OnPropertyChanged(nameof(CurrentPrayerSchedule));
                }
            }
        };
        clockTimer.Start();

        _prayerTimesService = prayerTimesService;
        _tasksService = tasksService;
        _lrclibService = lrclibService;
        _fileShelfService = fileShelfService;
        _fileShelfService.PhoneFileReceived += () => App.Current.Dispatcher.BeginInvoke(ShowSurprisedMoment);
        _fileShelfService.Changed += () => App.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!_fileShelfService.IsRunning && PhoneDropState == "Running") SetDropState("Stopped");
            OnPropertyChanged(nameof(DropServerUrl)); OnPropertyChanged(nameof(PhoneDropStatus)); OnPropertyChanged(nameof(IsPhoneDropRunning));
            if (!_fileShelfService.IsRunning || !_fileShelfService.HasPendingApproval)
            {
                _dropProposal = null; OnPropertyChanged(nameof(HasDropProposal)); OnPropertyChanged(nameof(DropProposalText));
            }
        });
        _fileShelfService.ApprovalRequested += proposal => App.Current.Dispatcher.BeginInvoke(() =>
        {
            _dropProposal = proposal; ShowFileShelf(false);
            OnPropertyChanged(nameof(HasDropProposal)); OnPropertyChanged(nameof(DropProposalText));
        });
        _clipboardService = clipboardService;
        _mediaService = mediaService;
        _mailService = mailService;
        _gowaService = gowaService;
        _discordService = discordService;
        _weatherService = weatherService;
        _pomodoroService = pomodoroService;

        _settings = initialSettings ?? SettingsService.Load();
        MochiDock.Core.DateDisplay.Set(MochiDock.Core.DateDisplay.Parse(_settings.DateFormat));
        Controls.MascotControl.CurrentBadgeStyle = MochiDock.Core.MascotBadge.ParseStyle(_settings.MascotBadgeStyle);
        _aiUsageService = new AiUsageService { HiddenProviders = _settings.HiddenAiProviders ?? new() };
        _aiUsageService.AlertsEnabled = _settings.EnableAiUsageAlerts;
        _aiUsageService.LowUsageDetected += OnAiLowUsage;
        SoundService.Enabled = _settings.EnableSounds;

        _weatherPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(20)
        };
        _weatherPollTimer.Tick += async (s, e) =>
        {
            try { await PollWeatherAsync(); }
            catch (Exception ex) { AppLog.Warn("Weather", ex); }
        };
        _weatherPollTimer.Start();
        _ = PollWeatherAsync();

        _idleTypewriterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(75) };
        _idleTypewriterTimer.Tick += (s, e) => OnTypewriterTick();
        // _idleTypewriterTimer.Start(); -- Self-talking typewriter engine disabled to keep clean resting clock

        _inactivitySleepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        // Resting and sleeping follow real keyboard and mouse idle time, not only the pointer on the dock (UpdateRestingFace).
        _inactivitySleepTimer.Tick += (s, e) => UpdateRestingFace(_wasGaming);
        _inactivitySleepTimer.Start();

        _pomodoroService.Tick += () =>
        {
            App.BeginOnUI(() =>
            {
                OnPropertyChanged(nameof(PomodoroTimeDisplay));
                OnPropertyChanged(nameof(PomodoroProgress));
                OnPropertyChanged(nameof(PomodoroPillText));
                OnPropertyChanged(nameof(CompactCapsuleText));
            });
        };

        _pomodoroService.StateChanged += () =>
        {
            App.BeginOnUI(() =>
            {
                OnPropertyChanged(nameof(PomodoroTimeDisplay));
                OnPropertyChanged(nameof(PomodoroProgress));
                OnPropertyChanged(nameof(PomodoroStateText));
                OnPropertyChanged(nameof(PomodoroModeLabel));
                OnPropertyChanged(nameof(PomodoroPlayPauseIcon));
                OnPropertyChanged(nameof(PomodoroPlayPauseLabel));
                OnPropertyChanged(nameof(IsPomodoroInMainPill));
                OnPropertyChanged(nameof(PomodoroPillText));
                RaiseCapsuleStateChanged();
            });
        };

        _pomodoroService.Completed += (finishedState) =>
        {
            App.BeginOnUI(() =>
            {
                SoundService.PlayNotificationSound();
                MascotMood = MascotMood.Happy;
                ShowMoment(MascotMood.Cheering, TimeSpan.FromSeconds(5));   // no-op while gaming, locked or screen off: the plain happy face stays then
                ShowPomodoro(false);
            });
        };
        _snoozedUntil = _settings.SnoozeUntilTicks > DateTime.Now.Ticks ? new DateTime(_settings.SnoozeUntilTicks) : DateTime.MinValue;
        CapsuleTheme = _settings.CapsuleTheme;
        CapsuleScrollSpeed = _settings.CapsuleScrollSpeed;
        ApplyCapsuleTheme(_settings.CapsuleTheme);
        InitializeTaskbar();

        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            await _gowaService.CheckConnectionAsync();
            App.BeginOnUI(() => OnPropertyChanged(nameof(IntegrationStatus)));
        });

        // Restore persisted states
        _isStatsPinnedToCapsule = _settings.IsStatsPinnedToCapsule;
        _isPinned = _settings.IsExpandedPinned;
        _hardwareService.IsGamingMode = _settings.IsHardwareGamingMode;
        _hardwareService.PingHost = MochiDock.Core.SettingsValidation.TryNormalizePingHost(_settings.PingHost, out var startupPingHost) ? startupPingHost : "1.1.1.1";

        _settings.LastActiveCardType = ValidManualCard();
        _settings.ActiveSystemModule = MochiDock.Core.NavigationPolicy.Module(_settings.ActiveSystemModule);
        _activeCardType = Enum.Parse<IslandCardType>(_settings.LastActiveCardType);

        _prayerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _prayerTimer.Tick += (s, e) => UpdatePrayerSchedule();
        _prayerTimer.Start();
        _ = UpdatePrayerLocationAndScheduleAsync();

        _mediaService.MediaUpdated += OnMediaUpdated;
        _mediaService.TimelineUpdated += OnTimelineUpdated;
        _mailService.EmailReceived += OnEmailReceived;
        _gowaService.MessageReceived += OnGowaMessageReceived;
        _gowaService.MessageReadOrDismissed += OnGowaMessageReadOrDismissed;
        _gowaService.AccountsChanged += () => App.Current.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(HasMultipleWhatsAppAccounts));
            OnPropertyChanged(nameof(IntegrationStatus));
        });
        _discordService.MessageReceived += OnDiscordMessageReceived;
        _discordService.AppMessageReceived += OnAppMessageReceived;
        _discordService.RecentAppsChanged += () => App.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(UnwatchedRecentApps)));
        _discordService.SetWatchedApps(WatchedApps);
        InitGameSessions();
        InitPlaytime();
        // Started in the morning: a few sleepy seconds, once the startup work is done.
        if (MochiDock.Core.MascotTriggers.WakingOnStart(DateTime.Now)) App.Current.Dispatcher.BeginInvoke(new Action(ShowWakingMoment), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        MascotControl.AnyMochiPetted += OnMochiPetted;

        _prayerArrivalTimer.Tick += (_, _) => { _prayerArrivalTimer.Stop(); _prayerArrivalUntil = default; NotifyPrayerArrival(); };
        _hardwareNoticeTimer.Tick+=(_,_)=>{_hardwareNoticeTimer.Stop();_hardwareNoticeUntil=default;_noticeIsAi=false;_noticeIsAgenda=false;_noticeIsGame=false;_noticeIsDevice=false;_noticeIsWeather=false;_noticeIsUpdate=false;NotifyHardwareNotice();};
        _autoCollapseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _autoCollapseTimer.Tick += (s, e) =>
        {
            if(!_isPinned && (IsDockInteractionActive?.Invoke()??false)){IsAutoCollapseDeferred=true;_autoCollapseTimer.Stop();return;}
            if (!_isPinned)
            {
                IsExpanded = false;
                _autoCollapseTimer.Stop();
            }
        };

        _hardwarePollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _hardwarePollTimer.Tick += (s, e) =>
        {
            if (_hardwareNoticeUntil != default && DateTimeOffset.UtcNow >= _hardwareNoticeUntil) { _hardwareNoticeUntil = default; NotifyHardwareNotice(); }
            if (_hardwarePollInFlight) return;
            _hardwarePollInFlight = true;

            bool locked = _isWorkstationLocked;
            bool awake = _displayAwake;
            bool statsVisible = _isExpanded && _activeCardType == IslandCardType.Hardware;
            bool pinned = IsStatsShownInCapsule;
            bool gamingMode = _settings.IsHardwareGamingMode;
            bool alerts = EnableHardwareAlerts;
            bool sessionSummary = _settings.EnableGameSessionSummary;

            // Readings are taken whenever something needs them (Stats open, readout pinned, a game, or hardware alerts, which are
            // on by default); each one also feeds the 2-minute Stats charts, so they keep recording while the card is closed.
            // Only the heavy part (chart shapes, per-core grid, top processes, drives) waits for the card (includeHeavy).
            _ = Task.Run(() =>
            {
                bool isGaming = _hardwareService.IsGameRunning();
                _hardwareService.SetGameCapture(!locked && awake && isGaming && (gamingMode || sessionSummary));
                bool sample = MochiDock.Core.LocalFeaturePolicy.SampleHardware(locked, awake, statsVisible, pinned, isGaming && gamingMode, alerts);
                HardwareStats? stats = sample ? _hardwareService.GetCurrentStats(includeHeavy: statsVisible, isGamingActiveOverride: isGaming) : null;
                return (isGaming, stats);
            }).ContinueWith(t =>
            {
                App.BeginOnUI(() =>
                {
                    try
                    {
                        if (t.IsFaulted || t.IsCanceled)
                        {
                            if (t.Exception != null) AppLog.Warn("Hardware", t.Exception);
                            return;
                        }
                        var (isGaming, stats) = t.Result;
                        bool gamingStateChanged = _wasGaming != isGaming;

                        // Adaptive polling frequency: 300ms during games, 1000ms while stats card open or active, 2000ms idle
                        bool nowStatsVisible = _isExpanded && _activeCardType == IslandCardType.Hardware;
                        var targetInterval = isGaming ? TimeSpan.FromMilliseconds(300) : (nowStatsVisible ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(2));
                        if (_hardwarePollTimer.Interval != targetInterval)
                        {
                            _hardwarePollTimer.Interval = targetInterval;
                        }

                        // Detect Game Transitions
                        if (gamingStateChanged)
                        {
                            if (isGaming)
                            {
                                MascotMood = MascotMood.Focused;
                            }
                            else
                            {
                                if (_queuedGamingWhatsAppCount > 0 || _queuedGamingEmailCount > 0 || _queuedGamingDiscordCount > 0 || _queuedGamingAppsCount > 0 || HasFreshGameSession())
                                {
                                    ShowGamingSummary();
                                }
                                else
                                {
                                    MascotMood = CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle;
                                }
                            }
                        }
                        UpdatePlaytime(isGaming, gamingStateChanged);
                        if (gamingStateChanged) { _gameTrouble.Reset(); _mascotGameTrouble = false; }
                        RefreshGamingFace(isGaming);
                        ExpireGamingPassThrough();
                        UpdateGameProfile(isGaming, gamingStateChanged);
                        UpdateLolStats(isGaming);
                        UpdateRestingFace(isGaming);
                        _wasGaming = isGaming;

                        if (stats != null)
                        {
                            HardwareStats = stats;
                            NoteFullSample();
                            UpdateMascotFromHardware(HardwareStats);
                            if (EnableHardwareAlerts) CheckHardwareAlerts();
                            SamplePowerTick();
                        }
                        else if (_mascotGpuHot || _mascotOverloaded)
                        {
                            // No fresh readings while sampling is paused: the hot and dizzy faces must not stay on.
                            _mascotGpuHot = _mascotOverloaded = false;
                            _mascotLoadTracker.Update(DateTime.Now, double.NaN, double.NaN);
                            RefreshMascotMood();
                        }

                        if (gamingStateChanged)
                        {
                            OnPropertyChanged(nameof(IsStatsShownInCapsule));
                            OnPropertyChanged(nameof(IsGamingSessionActive));
                            OnPropertyChanged(nameof(IsAudioWaveformVisible));
                            OnPropertyChanged(nameof(IsCompactMarqueeEnabled));
                            OnPropertyChanged(nameof(CompactCapsuleText));
                            OnPropertyChanged(nameof(CompactCapsuleIconType));
                            OnPropertyChanged(nameof(IsCompactTextVisible));
                            OnPropertyChanged(nameof(HasCompanionBubble));
                            OnPropertyChanged(nameof(HasMultipleActivities));
                            OnPropertyChanged(nameof(HasSingleNotification));
                        }
                    }
                    finally
                    {
                        _hardwarePollInFlight = false;
                    }
                });
            }, TaskScheduler.Default);
        };
        _hardwarePollTimer.Start();
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                if(!value)IsAutoCollapseDeferred=false;
                OnPropertyChanged();
                UpdateActivityFace();
                RefreshHistoryCharts(force: true);
                if (!_isExpanded && !_isPinned)
                {
                    _autoCollapseTimer.Stop();
                }
            }
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                UpdateSettings(x => x.IsExpandedPinned = value);

                OnPropertyChanged();
                OnPropertyChanged(nameof(PinButtonBackground));
                OnPropertyChanged(nameof(PinButtonForeground));
                OnPropertyChanged(nameof(PinToolTip));
            }
        }
    }

    public string PinButtonBackground => _isPinned ? "#0A84FF" : "#25252A";
    public string PinButtonForeground => _isPinned ? "#FFFFFF" : "#E0E0E6";
    public string PinToolTip => _isPinned ? "Unpin Island" : "Pin Island Open";

    private readonly Action<AppSettings> _saveNavigationSettings;

    private string ValidManualCard() => MochiDock.Core.NavigationPolicy.Card(_settings.LastActiveCardType, ValidCardAvailable);

    public void NavigateManually(IslandCardType card)
    {
        string selected = MochiDock.Core.NavigationPolicy.Card(card.ToString(), ValidCardAvailable);
        _isTransientCompanionView = false;
        _cardBeforeNotificationTakeover = null;
        if (_settings.LastActiveCardType != selected)
        {
            _settings.LastActiveCardType = selected;
            _saveNavigationSettings(_settings);
        }
        ActiveCardType = Enum.Parse<IslandCardType>(selected);
    }

    private bool ValidCardAvailable(string name) => name switch
    {
        "WhatsApp" => HasActiveWhatsApp, "Email" => HasActiveEmail, "Discord" => HasActiveDiscord, "Apps" => HasActiveApps,
        "Hardware" => ShowStatsTab, "Weather" => ShowWeatherTab, "Pomodoro" => ShowPomodoroTab,
        "Prayer" => EnablePrayerTimes, "FileShelf" => ShowFileShelfTab,
        "Clipboard" => ShowClipboardTab, "Today" => ShowTodayTab, "FxRates" => ShowFxRatesTab,
        "AiUsage" => ShowAiUsageTab,
        "Script" => ShowScriptTab, "Downloads" => ShowDownloadsTab,
        _ => true
    };
    public IslandCardType ActiveCardType
    {
        get => _activeCardType;
        set
        {
            if (_activeCardType != value)
            {
                var oldType = _activeCardType;
                _activeCardType = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(HeaderBadgeText));
                OnPropertyChanged(nameof(HeaderBadgeBrush));
                NotifyActiveCardState(oldType);
                NotifyActiveCardState(value);
                UpdateActivityFace();
                RefreshHistoryCharts(force: true);
            }
        }
    }

    private void NotifyActiveCardState(IslandCardType card)
    {
        switch (card)
        {
            case IslandCardType.Media: OnPropertyChanged(nameof(IsMediaActive)); break;
            case IslandCardType.WhatsApp: OnPropertyChanged(nameof(IsWhatsAppActive)); break;
            case IslandCardType.Discord: OnPropertyChanged(nameof(IsDiscordActive)); break;
            case IslandCardType.Apps: OnPropertyChanged(nameof(IsAppsActive)); break;
            case IslandCardType.Email: OnPropertyChanged(nameof(IsEmailActive)); break;
            case IslandCardType.Settings: OnPropertyChanged(nameof(IsSettingsActive)); break;
            case IslandCardType.Hardware: OnPropertyChanged(nameof(IsHardwareActive)); break;
            case IslandCardType.Today: OnPropertyChanged(nameof(IsTodayActive)); break;
            case IslandCardType.FxRates: OnPropertyChanged(nameof(IsFxRatesActive)); break;
            case IslandCardType.AiUsage: OnPropertyChanged(nameof(IsAiUsageActive)); break;
            case IslandCardType.Script: OnPropertyChanged(nameof(IsScriptActive)); break;
            case IslandCardType.Downloads: OnPropertyChanged(nameof(IsDownloadsActive)); break;
            case IslandCardType.Summary: OnPropertyChanged(nameof(IsSummaryActive)); break;
            case IslandCardType.Weather: OnPropertyChanged(nameof(IsWeatherActive)); break;
            case IslandCardType.Pomodoro: OnPropertyChanged(nameof(IsPomodoroActive)); break;
            case IslandCardType.Prayer: OnPropertyChanged(nameof(IsPrayerActive)); break;
            case IslandCardType.FileShelf: OnPropertyChanged(nameof(IsFileShelfActive)); break;
            case IslandCardType.Clipboard: OnPropertyChanged(nameof(IsClipboardActive)); break;
        }
    }


    public bool ShowTodayTab { get=>_settings.ShowTodayTab; set { UpdateSettings(x => x.ShowTodayTab = value); OnPropertyChanged(); if(!value&&ActiveCardType==IslandCardType.Today) ActiveCardType=IslandCardType.Media; } }
    public bool ShowJapanHolidays { get => _settings.ShowJapanHolidays; set { UpdateSettings(x => x.ShowJapanHolidays = value); OnPropertyChanged(); } }
    public bool DownloadAudioFormat { get => _settings.DownloadAudioFormat; set { if (_settings.DownloadAudioFormat == value) return; UpdateSettings(x => x.DownloadAudioFormat = value); OnPropertyChanged(); } }
    public bool WeekStartsOnMonday { get => _settings.WeekStartsOnMonday; set { UpdateSettings(x => x.WeekStartsOnMonday = value); OnPropertyChanged(); } }
    public bool ShowIndonesiaHolidays { get => _settings.ShowIndonesiaHolidays; set { UpdateSettings(x => x.ShowIndonesiaHolidays = value); OnPropertyChanged(); } }
    public bool HideInFullscreen { get=>_settings.HideInFullscreen; set { UpdateSettings(x => x.HideInFullscreen = value); OnPropertyChanged(); } }
    public bool EnablePowerAlerts { get=>_settings.EnablePowerAlerts; set { if(_settings.EnablePowerAlerts==value) return; UpdateSettings(x => x.EnablePowerAlerts = value); OnPropertyChanged(); } }

    // Charger / low-battery notices: the policy decides when, this gate decides whether the capsule may show one right now.
    private readonly MochiDock.Core.PowerNoticePolicy _powerPolicy=new();
    public event Action<MochiDock.Core.PowerNoticeKind,int>? PowerNoticeRequested;
    public void OnPowerState(MochiDock.Core.PowerState? state)
    {
        if(state is not {} s) return;
        UpdateSystemPower(s);
        _mascotBatteryLow = MochiDock.Core.MascotTriggers.BatteryLow(s, _mascotBatteryLow);
        RefreshMascotMood();
        var kind=_powerPolicy.Evaluate(s,DateTimeOffset.UtcNow);
        if(kind==MochiDock.Core.PowerNoticeKind.None||!CanShowPowerNotice()) return;
        PowerNoticeRequested?.Invoke(kind,s.Percent);
    }
    private bool CanShowPowerNotice()=>EnablePowerAlerts&&!_hardwarePresentationSuppressed&&!_isWorkstationLocked&&_displayAwake&&!_wasGaming&&!IsNotificationInMainPill&&!IsPrayerArrivalVisible&&!IsPrayerReminderVisible&&ShouldShowNotification("Power");

    public bool EnableHardwareAlerts { get=>_settings.EnableHardwareAlerts; set { UpdateSettings(x => x.EnableHardwareAlerts = value, SettingsApply.HardwareAlerts); OnPropertyChanged();_hardwareNoticeUntil=default;NotifyHardwareNotice(); } }
    private bool _displayAwake=true;
    public void StopHardwareCapture(){EndPlaytimeSession();EndGameProfile();_lol.Stop();StopPubgFetch();_hardwareService.SetGameCapture(false);_hardwareService.Dispose();}
    public void SetDisplayAwake(bool awake){Fx?.Configure(ShowFxRatesTab,_isWorkstationLocked||!awake);_deviceBatteries?.Configure(EnableDeviceBatteries,_isWorkstationLocked||!awake);if(!awake){_hardwareNoticeUntil=default;_hardwareAlerts.ResetContinuity();NotifyHardwareNotice();DismissPrayerArrival();}_displayAwake=awake;ApplyTaskbarSampler();_hardwareService.ResetSamplingBaseline();if(!awake)_hardwareService.SetGameCapture(false);}
    public bool IsBatteryModuleActive => ActiveSystemModule=="Battery";
    public FxRatesService? Fx {get;private set;}
    public void ConfigureFx(FxRatesService service){Fx=service;OnPropertyChanged(nameof(Fx));}
    /// <summary>The FX card's three pairs as saved (always normalized).</summary>
    public string[] FxPairs=>MochiDock.Core.FxPairPolicy.Normalize(_settings.FxPairs);
    /// <summary>Saves the FX card's pairs (normalized to three valid, distinct ones), applies them to the FX service and notifies the settings pages. Call on the UI thread.</summary>
    public void SetFxPairs(IEnumerable<string>? pairs)
    {
        var normalized=MochiDock.Core.FxPairPolicy.Normalize(pairs);
        UpdateSettings(s=>s.FxPairs=normalized.ToList());
        Fx?.SetPairs(normalized);
        OnPropertyChanged(nameof(FxPairs));
    }
    /// <summary>Read-only calendar feed behind the Today agenda (null until the app wires it up).</summary>
    public CalendarFeedService? CalendarFeed {get;private set;}
    public void ConfigureCalendar(CalendarFeedService service){CalendarFeed=service;OnPropertyChanged(nameof(CalendarFeed));ApplyReminderSettings();}
    public bool ShowFxRatesTab {get=>_settings.ShowFxRatesTab;set{UpdateSettings(x => x.ShowFxRatesTab = value);OnPropertyChanged();Fx?.Configure(value,_isWorkstationLocked||!_displayAwake);if(!value&&ActiveCardType==IslandCardType.FxRates)ActiveCardType=IslandCardType.Media;}}
    public bool IsFxRatesActive=>ActiveCardType==IslandCardType.FxRates;
    public bool IsTodayActive => ActiveCardType==IslandCardType.Today;
    public bool IsMediaActive => _activeCardType == IslandCardType.Media;
    public bool IsWhatsAppActive => _activeCardType == IslandCardType.WhatsApp;
    public bool IsEmailActive => _activeCardType == IslandCardType.Email;
    public bool IsDiscordActive => _activeCardType == IslandCardType.Discord;
    public bool IsSettingsActive => _activeCardType == IslandCardType.Settings;
    public bool IsHardwareActive => _activeCardType == IslandCardType.Hardware;
    public bool IsSummaryActive => _activeCardType == IslandCardType.Summary;
    public bool IsWeatherActive => _activeCardType == IslandCardType.Weather;
    public bool IsPomodoroActive => _activeCardType == IslandCardType.Pomodoro;
    public bool IsPrayerActive => _activeCardType == IslandCardType.Prayer;
    public bool IsFileShelfActive => _activeCardType == IslandCardType.FileShelf;
    public bool IsClipboardActive => _activeCardType == IslandCardType.Clipboard;
    public bool IsAiUsageActive => _activeCardType == IslandCardType.AiUsage;
    public bool IsScriptActive => _activeCardType == IslandCardType.Script;
    public bool IsDownloadsActive => _activeCardType == IslandCardType.Downloads;

    public bool AiUsageAlertsEnabled
    {
        get => _settings.EnableAiUsageAlerts;
        set { _aiUsageService.AlertsEnabled = value; UpdateSettings(x => x.EnableAiUsageAlerts = value); OnPropertyChanged(); }
    }

    public bool IsAiProviderEnabled(string id) =>
        !(_settings.HiddenAiProviders ?? new()).Contains(id, StringComparer.OrdinalIgnoreCase);

    public void SetAiProviderEnabled(string id, bool enabled)
    {
        UpdateSettings(x =>
        {
            var hidden = x.HiddenAiProviders ??= new();
            hidden.RemoveAll(p => string.Equals(p, id, StringComparison.OrdinalIgnoreCase));
            if (!enabled) hidden.Add(id);
            _aiUsageService.HiddenProviders = hidden;
        });
        _ = _aiUsageService.RefreshAllAsync(force: false);
    }

    // Low-usage notice: reuses the capsule notice slot of the hardware alerts, but opens the AI tab and does not need hardware alerts on.
    private bool _noticeIsAi;
    private void OnAiLowUsage(MochiDock.Core.AiUsageAlertReading reading)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        dispatcher.BeginInvoke(() =>
        {
            if (!_settings.EnableAiUsageAlerts || _hardwarePresentationSuppressed || _wasGaming || _isWorkstationLocked || !_displayAwake ||
                IsNotificationInMainPill || IsPrayerReminderVisible || !ShouldShowNotification("Hardware")) return;
            _noticeIsAi = true;
            _noticeIsAgenda = false;
            _noticeIsGame = false;
            _noticeIsDevice = false; _noticeIsWeather = false; _noticeIsUpdate = false;
            _hardwareNoticeText = $"{reading.ProviderName} {reading.LeftPercent:0}% left";
            _hardwareNoticeUntil = DateTimeOffset.UtcNow.AddSeconds(8);
            _hardwareNoticeTimer.Stop(); _hardwareNoticeTimer.Start();
            NotifyHardwareNotice();
        });
    }

    public bool ShowAiUsageTab { get => _settings.ShowAiUsageTab; set { UpdateSettings(x => x.ShowAiUsageTab = value); OnPropertyChanged(); if (!value && ActiveCardType == IslandCardType.AiUsage) ActiveCardType = IslandCardType.Media; } }
    public bool ShowScriptTab { get => _settings.ShowScriptTab; set { UpdateSettings(x => x.ShowScriptTab = value); OnPropertyChanged(); if (!value && ActiveCardType == IslandCardType.Script) ActiveCardType = IslandCardType.Media; } }
    public bool ShowDownloadsTab { get => _settings.ShowDownloadsTab; set { UpdateSettings(x => x.ShowDownloadsTab = value); OnPropertyChanged(); if (!value && ActiveCardType == IslandCardType.Downloads) ActiveCardType = IslandCardType.Media; } }

    public double? IslandHorizontalRatio { get => _settings.IslandHorizontalRatio; set { UpdateSettings(x => x.IslandHorizontalRatio = value); OnPropertyChanged(); } }
    public bool ExcludeFromScreenCapture { get => _settings.ExcludeFromScreenCapture; set { UpdateSettings(x => x.ExcludeFromScreenCapture = value); OnPropertyChanged(); ExcludeFromScreenCaptureChanged?.Invoke(value); } }
    public event Action<bool>? ExcludeFromScreenCaptureChanged;
    public int ScriptWpm { get => _settings.ScriptWpm; set { UpdateSettings(x => x.ScriptWpm = value); OnPropertyChanged(); } }

    public ObservableCollection<TodoTaskItem> Tasks => _tasksService.Tasks;
    public ObservableCollection<ShelfFileItem> ShelfFiles => _fileShelfService.ShelfFiles;
    public ObservableCollection<ClipboardHistoryItem> ClipboardHistory => _clipboardService.History;
    public string DropServerUrl => _fileShelfService.DropUrl;
    public string PhoneDropStatus => _fileShelfService.Status;
    public bool IsPhoneDropRunning => _fileShelfService.IsRunning;
    public string[] PhoneDropAddresses => FileShelfService.GetAddresses();
    public string PhoneDropAddress { get => _fileShelfService.LocalIpAddress; set { _fileShelfService.LocalIpAddress = value; OnPropertyChanged(); } }
    private DropProposal? _dropProposal;
    public bool HasDropProposal => _dropProposal != null;
    public string DropProposalText => _dropProposal == null ? "" : string.Join("\n", _dropProposal.Files.Select(f => $"{f.Name} — {f.Size:N0} bytes"));
    public void AnswerDropProposal(bool approve)
    {
        if (_dropProposal != null) _fileShelfService.Answer(_dropProposal.Id, approve);
        _dropProposal = null;
        OnPropertyChanged(nameof(HasDropProposal)); OnPropertyChanged(nameof(DropProposalText));
    }
    public ClipboardService ClipboardServiceInstance => _clipboardService;

    // ───────────── Feature tab visibility toggles ─────────────
    public bool ShowStatsTab
    {
        get => _settings?.ShowStatsTab ?? true;
        set
        {
            if (_settings != null && _settings.ShowStatsTab != value)
            {
                UpdateSettings(x => x.ShowStatsTab = value);
                OnPropertyChanged();
                if (!value && ActiveCardType == IslandCardType.Hardware) ActiveCardType = IslandCardType.Media;
            }
        }
    }

    public bool ShowWeatherTab
    {
        get => _settings?.ShowWeatherTab ?? true;
        set
        {
            if (_settings != null && _settings.ShowWeatherTab != value)
            {
                UpdateSettings(x => x.ShowWeatherTab = value);
                OnPropertyChanged();
                if (!value && ActiveCardType == IslandCardType.Weather) ActiveCardType = IslandCardType.Media;
            }
        }
    }

    public bool ShowPomodoroTab
    {
        get => _settings?.ShowPomodoroTab ?? true;
        set
        {
            if (_settings != null && _settings.ShowPomodoroTab != value)
            {
                UpdateSettings(x => x.ShowPomodoroTab = value);
                OnPropertyChanged();
                if (!value && ActiveCardType == IslandCardType.Pomodoro) ActiveCardType = IslandCardType.Media;
            }
        }
    }

    public bool ShowFileShelfTab
    {
        get => _settings?.ShowFileShelfTab ?? true;
        set
        {
            if (_settings != null && _settings.ShowFileShelfTab != value)
            {
                UpdateSettings(x => x.ShowFileShelfTab = value);
                OnPropertyChanged();
                if (!value && ActiveCardType == IslandCardType.FileShelf) ActiveCardType = IslandCardType.Media;
            }
        }
    }

    public bool ShowClipboardTab
    {
        get => _settings?.ShowClipboardTab ?? true;
        set
        {
            if (_settings != null && _settings.ShowClipboardTab != value)
            {
                UpdateSettings(x => x.ShowClipboardTab = value);
                OnPropertyChanged();
                if (!value && ActiveCardType == IslandCardType.Clipboard) ActiveCardType = IslandCardType.Media;
            }
        }
    }

    public bool EnableSounds
    {
        get => _settings?.EnableSounds ?? true;
        set
        {
            if (_settings == null || _settings.EnableSounds == value) return;
            SoundService.Enabled = value;
            UpdateSettings(x => x.EnableSounds = value);
            OnPropertyChanged();
        }
    }

    /// <summary>Re-reads settings.json (after flushing pending saves) and applies it the same way an import does.</summary>
    public async Task ReloadSettingsAsync()
    {
        await SettingsService.FlushAsync();
        await ApplyImportedSettings(SettingsService.Load());
    }

    public HardwareStats HardwareStats
    {
        get => _hardwareStats;
        set
        {
            _hardwareStats = value;
            OnPropertyChanged();
            if (IsStatsShownInCapsule)
            {
                OnPropertyChanged(nameof(CompactCapsuleText));
                OnPropertyChanged(nameof(CompactCapsuleIconType));
            }
        }
    }

    // Multitasking Activity Tracking
    public bool HasActiveWhatsApp => _currentWhatsApp != null;
    public bool HasActiveEmail => _currentEmail != null;
    public bool HasActiveDiscord => _currentDiscord != null;
    public bool HasActiveMedia => _currentMedia.IsPlaying;
    public bool IsAudioWaveformVisible => !IsHardwareNoticeVisible && !IsPrayerReminderVisible && !IsPrayerArrivalVisible && _currentMedia.IsPlaying && !_wasGaming;
    public bool IsStatsShownInCapsule => _isStatsPinnedToCapsule || _wasGaming;

    public string ActiveSystemModule
    {
        get => MochiDock.Core.NavigationPolicy.Module(_settings.ActiveSystemModule);
        set
        {
            value = MochiDock.Core.NavigationPolicy.Module(value);
            if (_settings.ActiveSystemModule != value)
            {
                _settings.ActiveSystemModule = value;
                _saveNavigationSettings(_settings);
                NotifySystemModuleSelection();
            }
        }
    }

    private void NotifySystemModuleSelection()
    {
        OnPropertyChanged(nameof(ActiveSystemModule));
        OnPropertyChanged(nameof(IsCpuModuleActive));
        OnPropertyChanged(nameof(IsGpuModuleActive));
        OnPropertyChanged(nameof(IsMemoryModuleActive));
        OnPropertyChanged(nameof(IsDrivesModuleActive));
        OnPropertyChanged(nameof(IsNetworkModuleActive));
        OnPropertyChanged(nameof(IsGamesModuleActive));
        OnPropertyChanged(nameof(IsBatteryModuleActive));
        UpdateActivityFace();
                RefreshHistoryCharts(force: true);
    }

    public bool IsCpuModuleActive => ActiveSystemModule == "CPU";
    public bool IsGpuModuleActive => ActiveSystemModule == "GPU";
    public bool IsMemoryModuleActive => ActiveSystemModule == "Memory";
    public bool IsDrivesModuleActive => ActiveSystemModule == "Drives";
    public bool IsNetworkModuleActive => ActiveSystemModule == "Network";
    public bool IsGamesModuleActive => ActiveSystemModule == "Games";

    public void SelectSystemModule(string module)
    {
        ActiveSystemModule = module;
    }
    public bool IsGamingSessionActive => _wasGaming;
    public bool IsCompactMarqueeEnabled => HasActiveMedia || IsNotificationInMainPill;

    private bool _isStatsPinnedToCapsule;
    public bool IsStatsPinnedToCapsule
    {
        get => _isStatsPinnedToCapsule;
        set
        {
            if (_isStatsPinnedToCapsule != value)
            {
                _isStatsPinnedToCapsule = value;
                UpdateSettings(x => x.IsStatsPinnedToCapsule = value);

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsStatsShownInCapsule));
                OnPropertyChanged(nameof(IsCompactMarqueeEnabled));
                OnPropertyChanged(nameof(CompactCapsuleText));
                OnPropertyChanged(nameof(CompactCapsuleIconType));
                OnPropertyChanged(nameof(IsCompactTextVisible));
                OnPropertyChanged(nameof(HasCompanionBubble));
                OnPropertyChanged(nameof(HasMultipleActivities));
                OnPropertyChanged(nameof(HasSingleNotification));
            }
        }
    }

    public void RaiseCapsuleStateChanged()
    {
        OnPropertyChanged(nameof(CompactLineSeconds));
        OnPropertyChanged(nameof(HasActiveWhatsApp));
        OnPropertyChanged(nameof(HasActiveDiscord));
        OnPropertyChanged(nameof(HasActiveApps));
        OnPropertyChanged(nameof(HasActiveEmail));
        OnPropertyChanged(nameof(HasActiveMedia));
        OnPropertyChanged(nameof(CanRetryLyrics));
        OnPropertyChanged(nameof(NotificationCount));
        OnPropertyChanged(nameof(IsNotificationInMainPill));
        OnPropertyChanged(nameof(HasSingleNotification));
        OnPropertyChanged(nameof(HasCompanionBubble));
        OnPropertyChanged(nameof(HasMultipleActivities));
        OnPropertyChanged(nameof(IsCompactTextVisible));
        OnPropertyChanged(nameof(CompactCapsuleText));
        OnPropertyChanged(nameof(CompactCapsuleIconType));
        OnPropertyChanged(nameof(CompanionBubbleType));
        OnPropertyChanged(nameof(CompanionBubbleBadge));
        OnPropertyChanged(nameof(CompanionBubbleColor));
        OnPropertyChanged(nameof(CompanionExtraCountText));
        OnPropertyChanged(nameof(HasCompanionExtraCount));
        OnPropertyChanged(nameof(RotationPositionText));
        OnPropertyChanged(nameof(ShowRotationPosition));
        OnPropertyChanged(nameof(IsNotificationTakeover));
        OnPropertyChanged(nameof(IsCompactMarqueeEnabled));
        OnPropertyChanged(nameof(IsWeatherInMainPill));
        OnPropertyChanged(nameof(WeatherPillText));
        OnPropertyChanged(nameof(IsPomodoroInMainPill));
        OnPropertyChanged(nameof(PomodoroPillText));
        OnPropertyChanged(nameof(IsBothPomodoroAndWeatherInMainPill));
        NotifyPrayerPresentation();
    }

    public bool IsCompactTextVisible => true;

    public string CompactCapsuleIconType
    {
        get
        {
            if (_wasGaming) return "Gaming";
            if (_isStatsPinnedToCapsule) return _hardwareStats.IsGamingMode ? "Gaming" : "Desktop";

            if (IsNotificationInMainPill && CurrentRotationItem.HasValue)
            {
                return CurrentRotationItem.Value switch
                {
                    IslandCardType.WhatsApp => "WhatsApp",
                    IslandCardType.Discord => "Discord",
                    IslandCardType.Apps => "Apps",
                    IslandCardType.Email => "Email",
                    _ => ""
                };
            }

            if (HasActiveMedia) return "Media";
            if (IsPomodoroInMainPill) return "Pomodoro";
            if (IsWeatherInMainPill) return "Weather";
            return "";
        }
    }

    /// <summary>Prefixes the sender on every non-empty line so per-line marquee cycling keeps context.</summary>
    private static string PrefixEachLine(string? sender, string? message)
    {
        var lines = (message ?? "").Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return $"{sender}: ";
        return string.Join("\n", lines.Select(l => $"{sender}: {l}"));
    }

    // The account that received the message is named only while several accounts are linked.
    private string? CapsuleWhatsAppSender()
    {
        var message = _currentWhatsApp;
        if (message == null) return null;
        return HasMultipleWhatsAppAccounts && !string.IsNullOrWhiteSpace(message.AccountName) ? $"{message.Sender} · {message.AccountName}" : message.Sender;
    }

    public string CompactCapsuleText
    {
        get
        {
            if(IsPrayerArrivalVisible)return PrayerArrivalTitle;
            if(IsPrayerReminderVisible)return PrayerReminderText;
            if(IsHardwareNoticeVisible)return HardwareNoticeText;
            if (_wasGaming || (_isStatsPinnedToCapsule && _hardwareStats.IsGamingMode))
            {
                if (GamingCapsuleNotice() is { } gamingNotice) return gamingNotice;
                // Compact game readout: [FPS fps | GPU X% · Y° | CPU Z%]
                string pingPart = _settings.ShowPingInGameCapsule && _hardwareStats.NetworkPingMs >= 0 ? $"{_hardwareStats.NetworkPingMs} ms  |  " : "";
                string fpsPart = (_hardwareStats.CurrentFps > 0 ? $"{_hardwareStats.CurrentFps} fps  |  " : "") + pingPart;
                string gpuTempPart = _hardwareStats.GpuTemp > 0 && _hardwareStats.IsGpuTempMeasured ? $" · {_hardwareStats.GpuTemp:0}°" : "";
                return $"{fpsPart}GPU {_hardwareStats.GpuUsage:0}%{gpuTempPart}  |  CPU {_hardwareStats.CpuUsage:0}%";
            }

            if (_isStatsPinnedToCapsule)
            {
                return $"CPU {_hardwareStats.CpuUsage:0}% · RAM {_hardwareStats.UsedRamGb:0.#} / {_hardwareStats.TotalRamGb:0} GB";
            }

            if (IsNotificationInMainPill && CurrentRotationItem.HasValue)
            {
                return CurrentRotationItem.Value switch
                {
                    IslandCardType.WhatsApp => PrefixEachLine(CapsuleWhatsAppSender(), _currentWhatsApp?.Message),
                    IslandCardType.Discord => PrefixEachLine(_currentDiscord?.Sender, _currentDiscord?.Message),
                    IslandCardType.Apps => PrefixEachLine(_currentApp?.Title, _currentApp?.Message),
                    IslandCardType.Email => $"{_currentEmail?.Sender}: {(_currentEmail?.Subject ?? "").Replace("\r", " ").Replace("\n", " ")}",
                    _ => ""
                };
            }

            if (HasActiveMedia && _currentMedia.IsPlaying)
            {
                if (!string.IsNullOrWhiteSpace(_currentLyricLine)) { return _currentLyricLine; } return MochiDock.Core.MediaPresentation.TitleArtist(_currentMedia.Title,_currentMedia.Artist);
            }

            if (IsPomodoroInMainPill)
            {
                return PomodoroPillText;
            }

            if (IsWeatherInMainPill)
            {
                return CurrentTimeText;
            }
            if (IsPrayerInMainPill)
            {
                return PrayerPillCountdownText;
            }

            // Resting state: the digital clock next to Mochi
            if (!_isExpanded && !_wasGaming && !_isStatsPinnedToCapsule)
            {
                return CurrentTimeText;
            }

            return "";
        }
    }

    public void TogglePinStatsToCapsule()
    {
        IsStatsPinnedToCapsule = !IsStatsPinnedToCapsule;
    }

    public string HeaderBadgeText => _activeCardType switch
    {
        IslandCardType.WhatsApp => "WHATSAPP",
        IslandCardType.Discord => "DISCORD",
        IslandCardType.Apps => string.IsNullOrWhiteSpace(_currentApp?.AppName) ? "APPS" : _currentApp.AppName.ToUpperInvariant(),
        IslandCardType.Email => string.IsNullOrEmpty(_currentEmail?.ClientSource) ? "EMAIL" : _currentEmail.ClientSource.ToUpperInvariant(),
        IslandCardType.Settings => "SETTINGS",
        IslandCardType.Hardware => "HARDWARE",
        IslandCardType.Weather => "WEATHER",
        IslandCardType.Pomodoro => "POMODORO",
        _ => string.IsNullOrWhiteSpace(_currentMedia.AppSource) ? "MEDIA" : _currentMedia.AppSource.ToUpperInvariant()
    };

    public string HeaderBadgeBrush => _activeCardType switch
    {
        IslandCardType.WhatsApp => "#25D366",
        IslandCardType.Discord => "#5865F2",
        IslandCardType.Apps => "#8E8E98",
        IslandCardType.Email => "#0A84FF",
        IslandCardType.Settings => "#9E9EA8",
        IslandCardType.Hardware => "#FF9F0A",
        IslandCardType.Weather => "#0A84FF",
        IslandCardType.Pomodoro => "#FF5252",
        _ => "#1DB954" // Accent green
    };

    /// <summary>The face Mochi shows. Setting it asks for a mood; <see cref="MochiDock.Core.MascotMoodPolicy"/> decides what is shown.</summary>
    public MascotMood MascotMood
    {
        get => _mascotMood;
        set
        {
            _baseMood = value;
            RefreshMascotMood();
        }
    }

    /// <summary>Works out the face from the requested mood and the ambient inputs, and raises a change when it differs.</summary>
    public void RefreshMascotMood()
    {
        var mood = MochiDock.Core.MascotMoodPolicy.Choose(new MochiDock.Core.MascotInputs(
            _baseMood, _settings?.ExpressiveMochi ?? true, _mascotOverdue, _mascotGpuHot || _mascotSustainedHot, _mascotOverloaded, _mascotBatteryLow, _mascotWeather, _mascotPetted, _isWorkstationLocked || !_displayAwake, _mascotActivity, _mascotDay));
        if (_mascotMood == mood) return;
        _mascotMood = mood;
        OnPropertyChanged(nameof(MascotMood));
    }

    private readonly MochiDock.Core.HardwareAlertPolicy _hardwareAlerts=new();
    private readonly DispatcherTimer _hardwareNoticeTimer=new(){Interval=TimeSpan.FromSeconds(8)};
    public void StopPresentationTimers(){StopTaskbarSampler();_aiUsageService.Dispose();_hardwareNoticeTimer.Stop();StopStatsHistory();_prayerArrivalTimer.Stop();_autoCollapseTimer.Stop();_hardwarePollTimer.Stop();}
    private DateTimeOffset _hardwareNoticeUntil;
    private string _hardwareNoticeText="";
    private bool _noticeIsWelcome;
    private bool _hardwarePresentationSuppressed;
    public void SetHardwarePresentationSuppressed(bool suppressed){_hardwarePresentationSuppressed=suppressed;if(suppressed){_hardwareNoticeUntil=default;NotifyHardwareNotice();}}
    public bool IsAutoCollapseDeferred {get;set;}
    public Func<bool>? IsDockInteractionActive {get;set;}
    public bool IsHardwareNoticeVisible => !_hardwarePresentationSuppressed && (EnableHardwareAlerts || _noticeIsAi || _noticeIsAgenda || _noticeIsGame || _noticeIsDevice || _noticeIsWeather || _noticeIsUpdate) && !_isWorkstationLocked && _displayAwake && !_wasGaming && !IsNotificationInMainPill && !IsPrayerReminderVisible && ShouldShowNotification("Hardware") && DateTimeOffset.UtcNow<_hardwareNoticeUntil;
    public string HardwareNoticeText => _hardwareNoticeText;
    private void NotifyHardwareNotice(){OnPropertyChanged(nameof(IsHardwareNoticeVisible));OnPropertyChanged(nameof(HardwareNoticeText));OnPropertyChanged(nameof(CompactCapsuleText));OnPropertyChanged(nameof(HasCompactLyricRomaji));OnPropertyChanged(nameof(IsAudioWaveformVisible));}
    public bool IsFirstRunWelcomeVisible => MochiDock.Core.OnboardingPolicy.ShouldShowFirstRunWelcome(_settings?.OnboardingDismissed ?? true);
    public void DismissFirstRunWelcome()
    {
        // Got it always hides the banner: if the flag is already set (an import or reload set it while the banner was showing),
        // only the save is skipped, never the notifications below, so the button can never look dead.
        if (!_settings.OnboardingDismissed) UpdateSettings(x => x.OnboardingDismissed = true, SettingsApply.None);
        if (_noticeIsWelcome)
        {
            _noticeIsWelcome = false;
            _hardwareNoticeUntil = default;
            NotifyHardwareNotice();
        }
        OnPropertyChanged(nameof(IsFirstRunWelcomeVisible));
    }
    public void ShowFirstRunCapsuleNotice()
    {
        if (!IsFirstRunWelcomeVisible || IsNotificationInMainPill || IsPrayerArrivalVisible || IsPrayerReminderVisible || IsHardwareNoticeVisible) return;
        _noticeIsAi = false; _noticeIsAgenda = false; _noticeIsGame = false; _noticeIsDevice = false; _noticeIsWeather = false; _noticeIsUpdate = false; _noticeHeatModule = null;
        _noticeIsWelcome = true;
        _hardwareNoticeText = "Welcome to MochiDock · Hover to explore";
        _hardwareNoticeUntil = DateTimeOffset.UtcNow.AddSeconds(12);
        _hardwareNoticeTimer.Stop();
        _hardwareNoticeTimer.Start();
        NotifyHardwareNotice();
    }
    public void OpenHardwareNotice(){bool ai=_noticeIsAi,agenda=_noticeIsAgenda,device=_noticeIsDevice,weather=_noticeIsWeather,update=_noticeIsUpdate,welcome=_noticeIsWelcome;string? heat=_noticeHeatModule;_noticeHeatModule=null;if(heat!=null){_hardwareNoticeUntil=default;NotifyHardwareNotice();StatsChartSpan="24h";SelectSystemModule(heat);ShowHardware(false);return;}_hardwareNoticeUntil=default;_noticeIsAi=false;_noticeIsAgenda=false;_noticeIsGame=false;_noticeIsDevice=false;_noticeIsWeather=false;_noticeIsUpdate=false;_noticeIsWelcome=false;NotifyHardwareNotice();if(welcome){PrepareHoverExpand();IsExpanded=true;}else if(update)ShowSettings(false);else if(agenda){AcknowledgeShownReminders();ShowToday(false);}else if(weather)ShowWeather(false);else if(ai)ShowAiUsage(false);else{if(device)SelectSystemModule("Battery");ShowHardware(false);}}
    private void CheckHardwareAlerts()
    {
        var now=DateTimeOffset.UtcNow;
        bool wasVisible = IsHardwareNoticeVisible;
        string wasText = _hardwareNoticeText;
        if(!EnableHardwareAlerts||_isWorkstationLocked||!_displayAwake){if((!_noticeIsAi&&!_noticeIsAgenda&&!_noticeIsGame&&!_noticeIsDevice&&!_noticeIsWeather&&!_noticeIsUpdate&&!_noticeIsWelcome)||_isWorkstationLocked||!_displayAwake){_hardwareNoticeUntil=default;_noticeIsAi=false;_noticeIsAgenda=false;_noticeIsGame=false;_noticeIsDevice=false;_noticeIsWeather=false;_noticeIsUpdate=false;_noticeIsWelcome=false;}_hardwareAlerts.ResetContinuity();if(IsHardwareNoticeVisible!=wasVisible||!string.Equals(_hardwareNoticeText,wasText,StringComparison.Ordinal))NotifyHardwareNotice();return;}
        var s=HardwareStats;
        foreach(var metric in new[]{("CPU usage",s.CpuUsageReading,_settings.CpuAlertThreshold,"%"),("GPU usage",s.GpuUsageReading,_settings.GpuAlertThreshold,"%"),("CPU temperature",s.CpuTemperatureReading,_settings.TemperatureAlertThreshold,"°C"),("GPU temperature",s.GpuTemperatureReading,_settings.TemperatureAlertThreshold,"°C")}) {
            if(!_hardwareAlerts.Evaluate(metric.Item1,metric.Item2,metric.Item3,now))continue;
            if(_hardwarePresentationSuppressed||_wasGaming||IsNotificationInMainPill||IsPrayerReminderVisible||!ShouldShowNotification("Hardware"))continue;
            _noticeIsAi=false;_noticeIsAgenda=false;_noticeIsGame=false;_noticeIsDevice=false;_noticeIsWeather=false;_noticeIsUpdate=false;_noticeIsWelcome=false;_hardwareNoticeText=$"{metric.Item1} {metric.Item2!.Value:0}{metric.Item4}";_hardwareNoticeUntil=now.AddSeconds(8);_hardwareNoticeTimer.Stop();_hardwareNoticeTimer.Start();
        }
        if(_hardwarePresentationSuppressed||IsNotificationInMainPill||IsPrayerReminderVisible||!ShouldShowNotification("Hardware"))_hardwareNoticeUntil=default;
        if(IsHardwareNoticeVisible!=wasVisible||!string.Equals(_hardwareNoticeText,wasText,StringComparison.Ordinal))NotifyHardwareNotice();
    }

    public void SetCapsuleScrollSpeed(double speed)
    {
        CapsuleScrollSpeed = Math.Clamp(speed, 10, 100);
        UpdateSettings(x => x.CapsuleScrollSpeed = CapsuleScrollSpeed);
    }

    /// <summary>The Mascot setting (Auto, Mochi, Kuro or Shiro): saves it and redraws every mascot.</summary>
    public void ApplyMascotChoice(string choice)
    {
        choice = MochiDock.Core.MascotChoice.Normalize(choice);
        UpdateSettings(x => x.Mascot = choice, SettingsApply.Theme);
    }

    /// <summary>The Mascot status badge style (Charm, Bubble, Sticker or Off): saves it and redraws every mascot.</summary>
    public void ApplyMascotBadgeStyle(string style)
    {
        style = MochiDock.Core.MascotBadge.NormalizeStyle(style);
        Controls.MascotControl.CurrentBadgeStyle = MochiDock.Core.MascotBadge.ParseStyle(style);
        UpdateSettings(x => x.MascotBadgeStyle = style, SettingsApply.None);
    }

    public void ApplyCapsuleTheme(string theme)
    {
        theme = MochiDock.Core.ThemePolicy.Normalize(theme);
        Controls.MascotControl.CurrentChoice = _settings.Mascot;   // the mascot picker; ThemeService resolves Auto against the theme
        Controls.MascotControl.CurrentBadgeStyle = MochiDock.Core.MascotBadge.ParseStyle(_settings.MascotBadgeStyle);
        bool changed = !string.Equals(_settings.CapsuleTheme, theme, StringComparison.Ordinal);
        CapsuleTheme = theme;
        if (changed)
        {
            UpdateSettings(x => x.CapsuleTheme = theme, SettingsApply.None);
        }

        if (System.Windows.Application.Current?.MainWindow != null)
        {
            ThemeService.ApplyTheme(System.Windows.Application.Current.MainWindow, theme);
        }
        App.ApplySettingsWindowTheme(theme);
    }

    public void ToggleHardwareMode()
    {
        _hardwareService.IsGamingMode = !_hardwareService.IsGamingMode;
        UpdateSettings(x => x.IsHardwareGamingMode = _hardwareService.IsGamingMode);

        HardwareStats = _hardwareService.GetCurrentStats();
    }

    public string CurrentAudioDeviceName
    {
        get => _currentAudioDeviceName;
        set
        {
            if (_currentAudioDeviceName != value)
            {
                _currentAudioDeviceName = value;
                OnPropertyChanged();
            }
        }
    }

    public void RefreshAudioDeviceName()
    {
        try
        {
            var devices = _audioService.GetPlaybackDevices();
            var def = devices.FirstOrDefault(d => d.IsDefault);
            if (def != null)
            {
                CurrentAudioDeviceName = def.Name;
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
    }

    /// <summary>Every active playback device, the current default marked, for the audio output menu.</summary>
    public IReadOnlyList<AudioDeviceInfo> GetAudioDevices()
    {
        try { return _audioService.GetPlaybackDevices(); }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); return Array.Empty<AudioDeviceInfo>(); }
    }

    /// <summary>Makes the chosen device the default playback device.</summary>
    public void SelectAudioDevice(string deviceId)
    {
        try
        {
            var device = _audioService.GetPlaybackDevices().FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase));
            if (device != null && !device.IsDefault && _audioService.SetDefaultPlaybackDevice(device.Id)) CurrentAudioDeviceName = device.Name;
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
    }

    public void SwitchNextAudioDevice()
    {
        try
        {
            var devices = _audioService.GetPlaybackDevices();
            if (devices.Count <= 1) return;

            int currentIndex = devices.FindIndex(d => d.IsDefault);
            int nextIndex = (currentIndex + 1) % devices.Count;
            var nextDevice = devices[nextIndex];

            if (_audioService.SetDefaultPlaybackDevice(nextDevice.Id))
            {
                CurrentAudioDeviceName = nextDevice.Name;
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
    }

    public void ShowMedia(bool rememberSelection = true)
    {
        RefreshAudioDeviceName();
        if (rememberSelection) NavigateManually(IslandCardType.Media); else ActiveCardType = IslandCardType.Media;
        MascotMood = CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle;
        IsExpanded = true;
    }

    public void ShowWhatsApp(bool rememberSelection = true)
    {
        if (rememberSelection) NavigateManually(IslandCardType.WhatsApp); else ActiveCardType = IslandCardType.WhatsApp;
        MascotMood = MascotMood.Alert;
        IsExpanded = true;
    }

    public void ShowEmail(bool rememberSelection = true)
    {
        if (rememberSelection) NavigateManually(IslandCardType.Email); else ActiveCardType = IslandCardType.Email;
        MascotMood = MascotMood.Alert;
        IsExpanded = true;
    }

    private int _weatherPollGeneration;
    private CancellationTokenSource? _weatherPollCts;

    public Task<LocationGeocodeResult> ResolveLocationAsync(string query, CancellationToken cancellationToken = default) =>
        _weatherService.ResolveLocationAsync(query, cancellationToken);

    public async Task PollWeatherAsync()
    {
        int generation = ++_weatherPollGeneration;
        _weatherPollCts?.Cancel();
        _weatherPollCts?.Dispose();
        var cts = new CancellationTokenSource();
        _weatherPollCts = cts;

        App.BeginOnUI(() =>
        {
            IsWeatherLoading = true;
            OnPropertyChanged(nameof(WeatherDisplayText));
            OnPropertyChanged(nameof(WeatherConditionText));
            OnPropertyChanged(nameof(WeatherCityDisplay));
        });

        string city = _settings?.WeatherCity ?? "Tokyo";
        WeatherInfo? weather = null;
        try
        {
            weather = await _weatherService.GetWeatherAsync(city, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Weather", ex);
        }
        finally
        {
            App.BeginOnUI(() =>
            {
                if (generation != _weatherPollGeneration) return;
                IsWeatherLoading = false;
                if (weather != null)
                {
                    WeatherInfo = weather;
                    _currentWeatherCity = city;
                    CheckRainSoon(weather);
                }
                OnPropertyChanged(nameof(WeatherDisplayText));
                OnPropertyChanged(nameof(WeatherConditionText));
                OnPropertyChanged(nameof(WeatherIconText));
                OnPropertyChanged(nameof(WeatherCityDisplay));
                OnPropertyChanged(nameof(CompactCapsuleText));
            });
        }
    }

    public async Task RefreshWeatherAsync()
    {
        await PollWeatherAsync();
    }

    public void ShowDiscord(bool rememberSelection = true)
    {
        if (rememberSelection) NavigateManually(IslandCardType.Discord); else ActiveCardType = IslandCardType.Discord;
        MascotMood = MascotMood.Alert;
        IsExpanded = true;
    }

    public void ShowSettings(bool rememberSelection = true)
    {
        if (rememberSelection) NavigateManually(IslandCardType.Settings); else ActiveCardType = IslandCardType.Settings;
        IsExpanded = true;
    }

    public void ShowWeather(bool rememberSelection = true)
    {
        if (rememberSelection) NavigateManually(IslandCardType.Weather); else ActiveCardType = IslandCardType.Weather;
        IsExpanded = true;
    }

    public void ShowPomodoro(bool rememberSelection = true)
    {
        if (rememberSelection) NavigateManually(IslandCardType.Pomodoro); else ActiveCardType = IslandCardType.Pomodoro;
        IsExpanded = true;
    }

    public void ShowPrayer(bool rememberSelection = true)
    {
        if (rememberSelection) NavigateManually(IslandCardType.Prayer); else ActiveCardType = IslandCardType.Prayer;
        IsExpanded = true;
    }

    public void ShowFileShelf(bool rememberSelection = true) { OnPropertyChanged(nameof(PhoneDropAddresses)); if (rememberSelection) NavigateManually(IslandCardType.FileShelf); else ActiveCardType = IslandCardType.FileShelf; IsExpanded = true; }
    public void ShowClipboard(bool rememberSelection = true) { if (rememberSelection) NavigateManually(IslandCardType.Clipboard); else ActiveCardType = IslandCardType.Clipboard; IsExpanded = true; }
    public void ShowAiUsage(bool rememberSelection = true) { if (rememberSelection) NavigateManually(IslandCardType.AiUsage); else ActiveCardType = IslandCardType.AiUsage; IsExpanded = true; }
    public void ShowScript(bool rememberSelection = true) { if (rememberSelection) NavigateManually(IslandCardType.Script); else ActiveCardType = IslandCardType.Script; IsExpanded = true; }
    public void ShowDownloads(bool rememberSelection = true) { if (rememberSelection) NavigateManually(IslandCardType.Downloads); else ActiveCardType = IslandCardType.Downloads; IsExpanded = true; }

    public void AddTask(string input, int? explicitMinutes = null) => _tasksService.AddTaskWithNaturalLanguage(input, explicitMinutes);
    public void ToggleTask(TodoTaskItem task)
    {
        _tasksService.ToggleTaskCompleted(task);
        UpdateOverdueMood();
        if (task.IsCompleted)
        {
            // The last task due today gets a cheer instead of the usual happy moment; the Proud face follows.
            if (TodaysTasksDone()) ShowMoment(MascotMood.Cheering, TimeSpan.FromSeconds(5)); else ShowHappyMoment();
        }
        UpdateDayFace(force: true);
    }
    public void DeleteTask(TodoTaskItem task) => _tasksService.DeleteTask(task);

    public void AddShelfFile(string path) => _fileShelfService.AddFile(path);
    public void RemoveShelfFile(ShelfFileItem item) => _fileShelfService.RemoveFile(item);
    public void ClearShelfFiles() => _fileShelfService.ClearShelf();
    public string PhoneDropState { get; private set; } = "Stopped";
    public bool CanStartPhoneDrop => PhoneDropState == "Stopped";
    public bool CanStopPhoneDrop => PhoneDropState is "Starting" or "Running" or "Faulted";
    private void SetDropState(string state)
    {
        PhoneDropState = state;
        OnPropertyChanged(nameof(PhoneDropState)); OnPropertyChanged(nameof(CanStartPhoneDrop));
        OnPropertyChanged(nameof(CanStopPhoneDrop)); OnPropertyChanged(nameof(IsPhoneDropRunning));
        OnPropertyChanged(nameof(PhoneDropStatus)); OnPropertyChanged(nameof(DropServerUrl));
    }
    public async Task StartPhoneDropServerAsync()
    {
        if (!CanStartPhoneDrop) return;
        SetDropState("Starting");
        try { await _fileShelfService.StartServerAsync(); SetDropState("Running"); }
        catch { SetDropState("Faulted"); throw; }
    }
    public async Task StopPhoneDropServerAsync()
    {
        if (PhoneDropState == "Stopping") return;
        SetDropState("Stopping");
        try { await _fileShelfService.StopServerAsync(); SetDropState("Stopped"); }
        catch { SetDropState("Faulted"); throw; }
    }

    public void RecallClipboard(ClipboardHistoryItem item) => _clipboardService.CopyToClipboard(item);
    public void TogglePinClipboard(ClipboardHistoryItem item) => _clipboardService.TogglePin(item);
    public void ClearUnpinnedClipboard() => _clipboardService.ClearUnpinned();

    public void ShowHardware(bool rememberSelection = true)
    {
        HardwareStats = _hardwareService.GetCurrentStats();
        if (rememberSelection) NavigateManually(IslandCardType.Hardware); else ActiveCardType = IslandCardType.Hardware;
        IsExpanded = true;
    }

    public void ShowGamingSummary()
    {
        var session = TakePendingGameSession();
        SummaryTitle = "🎮 Game Session Complete";
        SummarySubtitle = session != null ? $"{session.Game} · {MochiDock.Core.GameSessionFormat.Duration(session.Seconds)}" : "While you were gaming, you received:";
        SummaryWhatsAppCount = _queuedGamingWhatsAppCount;
        SummaryEmailCount = _queuedGamingEmailCount;
        SummaryIsMorning = false;

        var details = new List<string>();
        if (_queuedGamingWhatsAppCount > 0)
            details.Add($"{_queuedGamingWhatsAppCount} WhatsApp message{(_queuedGamingWhatsAppCount > 1 ? "s" : "")}");
        if (_queuedGamingDiscordCount > 0)
            details.Add($"{_queuedGamingDiscordCount} Discord message{(_queuedGamingDiscordCount > 1 ? "s" : "")}");
        if (_queuedGamingAppsCount > 0)
            details.Add(AppsCount(_queuedGamingAppsCount));
        if (_queuedGamingEmailCount > 0)
            details.Add($"{_queuedGamingEmailCount} email{(_queuedGamingEmailCount > 1 ? "s" : "")}");

        var lines = new List<string>();
        var rows = new List<SummaryLine>();
        if (session != null)
        {
            void Row(string label, string text, IReadOnlyList<MochiDock.Core.GameSessionFormat.SummaryStat>? stats = null) { lines.Add(text); rows.Add(new SummaryLine(label, text, stats)); }
            if (session.Match != null) Row("Match", MochiDock.Core.GameMatchFormat.Line(session.Match));
            Row("Speed", MochiDock.Core.GameSessionFormat.FpsLine(session), MochiDock.Core.GameSessionFormat.SpeedStats(session));
            Row("Frames", MochiDock.Core.GameSessionFormat.DetailLine(session), MochiDock.Core.GameSessionFormat.FrameStats(session));
            string gpuLine = MochiDock.Core.GameSessionFormat.HardwareLine(session);
            if (gpuLine.Length > 0) Row("GPU", gpuLine, MochiDock.Core.GameSessionFormat.GpuStats(session));
            string cpuLine = MochiDock.Core.GameSessionFormat.CpuLine(session);
            if (cpuLine.Length > 0) Row("CPU", cpuLine, MochiDock.Core.GameSessionFormat.CpuStats(session));
            string pingLine = MochiDock.Core.GameSessionFormat.PingLine(session);
            if (pingLine.Length > 0) Row("Network", pingLine, MochiDock.Core.GameSessionFormat.PingStats(session));
            if (session.TrendText.Length > 0) Row("Trend", session.TrendText);
        }
        SetSummaryGraph(session);
        if (details.Count > 0)
        {
            lines.Add((session != null ? "Received while gaming: " : "") + string.Join(" · ", details));
            if (session != null) rows.Add(new SummaryLine("Missed", string.Join(" · ", details)));
        }
        SummaryDetails = string.Join("\n", lines);
        SetSummaryLines(session != null ? rows : null);

        // Clear gaming queues
        _queuedGamingWhatsAppCount = 0;
        _queuedGamingEmailCount = 0;
        _queuedGamingDiscordCount = 0;
        _queuedGamingAppsCount = 0;
        _queuedGamingPreviews.Clear();

        SoundService.PlayNotificationSound();
        ActiveCardType = IslandCardType.Summary;
        MascotMood = MascotMood.Alert;
        IsExpanded = true;
    }

    public void HandleSessionLock()
    {
        _ = _fileShelfService.StopServerAsync();
        PrayerAudio.Stop(); _prayerReminders.Reset(DateTimeOffset.UtcNow);
        _mediaService.SetSuspended(true);
        _hardwareNoticeUntil=default;_hardwareAlerts.ResetContinuity();NotifyHardwareNotice();DismissPrayerArrival();
        Fx?.Configure(ShowFxRatesTab,true);_deviceBatteries?.Configure(EnableDeviceBatteries,true);
        _isWorkstationLocked = true;
        ApplyTaskbarSampler();   // the readout sampler pauses while the PC is locked
        NotifyPrayerPresentation();
        _hardwareService.SetGameCapture(false);
        _hardwareService.ResetSamplingBaseline();
        _lockTimestamp = DateTime.Now;
        _lockedWhatsAppCount = 0;
        _lockedEmailCount = 0;
        _lockedDiscordCount = 0;
        _lockedAppsCount = 0;
        _lockedPreviews.Clear();

        MascotMood = MascotMood.Sleeping;
    }

    public void HandleSessionUnlock()
    {
        if (!_isWorkstationLocked) return;
        _prayerReminders.Reset(DateTimeOffset.UtcNow);
        _mediaService.SetSuspended(false);
        _isWorkstationLocked = false;
        ApplyTaskbarSampler();
        NotifyPrayerPresentation();
        Fx?.Configure(ShowFxRatesTab,!_displayAwake);_deviceBatteries?.Configure(EnableDeviceBatteries,!_displayAwake);

        var awayDuration = DateTime.Now - _lockTimestamp;
        bool wakeUp = MochiDock.Core.MascotTriggers.WakingUp(awayDuration);
        MascotMood = MascotMood.Idle;

        // If user was away for at least 30 minutes, or if it's morning (05:00 - 11:59)
        int currentHour = DateTime.Now.Hour;
        bool isMorningTime = currentHour >= 5 && currentHour < 12;
        bool wasAwayLongEnough = awayDuration.TotalMinutes >= 30;

        if (wasAwayLongEnough || isMorningTime || _lockedWhatsAppCount > 0 || _lockedDiscordCount > 0 || _lockedAppsCount > 0 || _lockedEmailCount > 0)
        {
            if (isMorningTime)
            {
                SummaryTitle = "☀️ Good Morning!";
                SummarySubtitle = "While you were away overnight:";
                SummaryIsMorning = true;
            }
            else if (currentHour >= 12 && currentHour < 18)
            {
                SummaryTitle = "🌤️ Good Afternoon!";
                SummarySubtitle = "While you were away:";
                SummaryIsMorning = false;
            }
            else
            {
                SummaryTitle = "👋 Welcome Back!";
                SummarySubtitle = "While you were away:";
                SummaryIsMorning = false;
            }

            SetSummaryGraph(null);   // this summary is not about a game: nothing of the last one may stay
            SetSummaryLines(null);
            SummaryWhatsAppCount = _lockedWhatsAppCount;
            SummaryEmailCount = _lockedEmailCount;

            if (_lockedWhatsAppCount == 0 && _lockedDiscordCount == 0 && _lockedAppsCount == 0 && _lockedEmailCount == 0)
            {
                SummaryDetails = "All caught up! No missed messages. Have a wonderful day!";
            }
            else
            {
                var details = new List<string>();
                if (_lockedWhatsAppCount > 0)
                    details.Add($"{_lockedWhatsAppCount} WhatsApp message{(_lockedWhatsAppCount > 1 ? "s" : "")}");
                if (_lockedDiscordCount > 0)
                    details.Add($"{_lockedDiscordCount} Discord message{(_lockedDiscordCount > 1 ? "s" : "")}");
                if (_lockedAppsCount > 0)
                    details.Add(AppsCount(_lockedAppsCount));
                if (_lockedEmailCount > 0)
                    details.Add($"{_lockedEmailCount} unread email{(_lockedEmailCount > 1 ? "s" : "")}");
                SummaryDetails = string.Join(" · ", details);
            }

            // Reset lock counts
            _lockedWhatsAppCount = 0;
            _lockedEmailCount = 0;
            _lockedDiscordCount = 0;
            _lockedAppsCount = 0;
            _lockedPreviews.Clear();

            ActiveCardType = IslandCardType.Summary;
            MascotMood = MascotMood.Alert;
            IsExpanded = true;
        }
        else if (wakeUp) ShowWakingMoment();   // a summary (Alert) wins; otherwise a few yawning seconds
    }

    public void DismissSummary()
    {
        if (ActiveCardType == IslandCardType.Summary)
        {
            if (HasActiveWhatsApp)
                ActiveCardType = IslandCardType.WhatsApp;
            else if (HasActiveDiscord)
                ActiveCardType = IslandCardType.Discord;
            else if (HasActiveApps)
                ActiveCardType = IslandCardType.Apps;
            else if (HasActiveEmail)
                ActiveCardType = IslandCardType.Email;
            else
                ActiveCardType = ResolveNotificationDismissFallback();

            MascotMood = _wasGaming ? MascotMood.Focused : (CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle);
            IsExpanded = false;
        }
    }

    public float AdjustMasterVolume(float delta) => _audioService.AdjustMasterVolume(delta);
    public float GetMasterVolume() => _audioService.GetMasterVolume();
    public bool IsMuted() => _audioService.IsMuted();

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(CompactCapsuleText))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPrimaryCapsuleReading)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRestingClockPill)));
        }
    }
}
