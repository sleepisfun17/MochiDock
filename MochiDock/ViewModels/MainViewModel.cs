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
    private PrayerSchedule? _currentPrayerSchedule;
    private readonly DispatcherTimer _prayerTimer;
    private SongLyrics? _currentLyrics;
    private string _currentLyricLine = "";
    private string _lastFetchedTrack = "";
    private readonly MochiDock.Core.RequestGeneration _lyricRequests = new();
    private CancellationTokenSource? _lyricCancellation;
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

    // Pomodoro Widget
    public bool IsPomodoroInMainPill =>
        _pomodoroService != null && _pomodoroService.IsActive &&
        !HasActiveMedia && !IsStatsShownInCapsule && !IsNotificationInMainPill && !IsPrayerReminderVisible && !IsPrayerArrivalVisible;

    public bool IsBothPomodoroAndWeatherInMainPill => IsPomodoroInMainPill && IsWeatherInMainPill;

    public string PomodoroPillText => _pomodoroService != null && _pomodoroService.IsActive
        ? $"{_pomodoroService.FormattedTime} · {(_pomodoroService.State == PomodoroState.Break ? "Break" : (_pomodoroService.State == PomodoroState.Paused ? "Paused" : "Focus"))}"
        : "";

    public string PomodoroTimeDisplay => _pomodoroService?.FormattedTime ?? "25:00";
    public double PomodoroProgress
    {
        get => _pomodoroService?.ProgressPercentage ?? 0;
        set { }
    }
    public string PomodoroStateText => _pomodoroService?.State switch
    {
        PomodoroState.Working => "FOCUSING",
        PomodoroState.Break => "ON BREAK",
        PomodoroState.Paused => "PAUSED",
        _ => "POMODORO"
    };
    public string PomodoroModeLabel => _pomodoroService?.State switch
    {
        PomodoroState.Working => "Stay focused! You got this.",
        PomodoroState.Break => "Take a deep breath and stretch.",
        PomodoroState.Paused => "Timer paused.",
        _ => "25m Focus / 5m Break"
    };
    public string PomodoroPlayPauseIcon => (_pomodoroService?.IsRunning ?? false) ? "⏸" : "▶";
    public string PomodoroPlayPauseLabel => (_pomodoroService?.IsRunning ?? false) ? "Pause" : "Start";

    private readonly HardwareMonitorService _hardwareService = new();
    private readonly AudioEndpointService _audioService = new();
    private readonly DispatcherTimer _autoCollapseTimer;
    private readonly DispatcherTimer _hardwarePollTimer;

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

    private MediaInfo _currentMedia = new();
    private EmailNotification? _currentEmail;
    public ObservableCollection<EmailNotification> RecentEmailMessages { get; } = new();
    private WhatsAppNotification? _currentWhatsApp;
    public ObservableCollection<WhatsAppNotification> RecentWhatsAppMessages { get; } = new();
    public ObservableCollection<WhatsAppConversation> ActiveWhatsAppConversations { get; } = new();
    private WhatsAppConversation? _selectedWhatsAppConversation;
    public WhatsAppConversation? SelectedWhatsAppConversation
    {
        get => _selectedWhatsAppConversation;
        set
        {
            if (_selectedWhatsAppConversation != value)
            {
                _selectedWhatsAppConversation = value;
                OnPropertyChanged();
                if (value?.LatestMessage != null)
                {
                    CurrentWhatsApp = value.LatestMessage;
                }
            }
        }
    }
    private DiscordNotification? _currentDiscord;
    public ObservableCollection<DiscordNotification> RecentDiscordMessages { get; } = new();
    private HardwareStats _hardwareStats = new();
    private string _replyText = "";
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
        _gowaService.Start();
        _ = PollWeatherAsync();
        await Task.Delay(1200);
        await _gowaService.CheckConnectionAsync();
        OnPropertyChanged(nameof(IntegrationStatus));
    }

    private string _currentWeatherCity = "";

    public async void ApplyImportedSettings(AppSettings settings)
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
        OnPropertyChanged(nameof(EnableDeviceBatteries));OnPropertyChanged(nameof(ShowDeviceBatteriesOnTaskbar));OnPropertyChanged(nameof(ShowDeviceBatteryBackground));
        ApplyDeviceBatteryConfiguration();DeviceBatterySettingsChanged?.Invoke();
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
            _gowaService.Start();
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

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(MediaService mediaService, IMailService mailService, GowaService gowaService, DiscordNotificationService discordService, WeatherService weatherService, PomodoroService pomodoroService, PrayerTimesService prayerTimesService, LrclibService lrclibService, TasksService tasksService, FileShelfService fileShelfService, ClipboardService clipboardService, Action<AppSettings>? saveNavigationSettings = null)
    {
        _saveNavigationSettings = saveNavigationSettings ?? SettingsService.Save;
        var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clockTimer.Tick += (s, e) =>
        {
            OnPropertyChanged(nameof(CurrentTimeText));
            OnPropertyChanged(nameof(CurrentDateText));
            OnPropertyChanged(nameof(HeaderDateTimeText));
            UpdateActivityFace();   // Pomodoro state, FX change and the weather can change without navigation
            if (!_isExpanded && !HasActiveMedia && !IsStatsShownInCapsule && !IsNotificationInMainPill && !IsPomodoroInMainPill)
            {
                OnPropertyChanged(nameof(CompactCapsuleText));
            if (DateTime.Today != _lastCalendarDate)
            {
                _lastCalendarDate = DateTime.Today;
                CalendarDays = CalendarHelper.GenerateMonthDays(DateTime.Today);
                OnPropertyChanged(nameof(CalendarDays));
                OnPropertyChanged(nameof(CurrentDayOfWeekUpper));
                OnPropertyChanged(nameof(CurrentDateMonthText));
            }
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

        _settings = SettingsService.Load();
        MochiDock.Core.DateDisplay.Set(MochiDock.Core.DateDisplay.Parse(_settings.DateFormat));
        _aiUsageService = new AiUsageService { HiddenProviders = _settings.HiddenAiProviders ?? new() };
        _aiUsageService.AlertsEnabled = _settings.EnableAiUsageAlerts;
        _aiUsageService.LowUsageDetected += OnAiLowUsage;
        SoundService.Enabled = _settings.EnableSounds;

        _weatherPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(20)
        };
        _weatherPollTimer.Tick += async (s, e) => await PollWeatherAsync();
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
            App.Current.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(PomodoroTimeDisplay));
                OnPropertyChanged(nameof(PomodoroProgress));
                OnPropertyChanged(nameof(PomodoroPillText));
                OnPropertyChanged(nameof(CompactCapsuleText));
            });
        };

        _pomodoroService.StateChanged += () =>
        {
            App.Current.Dispatcher.Invoke(() =>
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
            App.Current.Dispatcher.Invoke(() =>
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

        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            await _gowaService.CheckConnectionAsync();
            App.Current.Dispatcher.Invoke(() => OnPropertyChanged(nameof(IntegrationStatus)));
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
        _hardwareNoticeTimer.Tick+=(_,_)=>{_hardwareNoticeTimer.Stop();_hardwareNoticeUntil=default;_noticeIsAi=false;_noticeIsAgenda=false;_noticeIsGame=false;_noticeIsDevice=false;_noticeIsWeather=false;NotifyHardwareNotice();};
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
            if(_hardwareNoticeUntil!=default && DateTimeOffset.UtcNow>=_hardwareNoticeUntil){_hardwareNoticeUntil=default;NotifyHardwareNotice();}
            bool isGaming = _hardwareService.IsGameRunning();
            _hardwareService.SetGameCapture(!_isWorkstationLocked && _displayAwake && isGaming && (_settings.IsHardwareGamingMode || _settings.EnableGameSessionSummary));
            bool gamingStateChanged = _wasGaming != isGaming;

            // Adaptive polling frequency: 300ms during games, 1000ms while stats card open or active, 2000ms idle
            bool statsVisible = _isExpanded && _activeCardType == IslandCardType.Hardware;
            var targetInterval = isGaming ? TimeSpan.FromMilliseconds(300) : (statsVisible ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(2));
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

            if (MochiDock.Core.LocalFeaturePolicy.SampleHardware(_isWorkstationLocked,_displayAwake,statsVisible,IsStatsShownInCapsule,isGaming && _settings.IsHardwareGamingMode,EnableHardwareAlerts))
            {
                HardwareStats = _hardwareService.GetCurrentStats();
                UpdateMascotFromHardware(HardwareStats);
                if(EnableHardwareAlerts) CheckHardwareAlerts();
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
                _settings.IsExpandedPinned = value;
                SettingsService.Save(_settings);

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


    public bool ShowTodayTab { get=>_settings.ShowTodayTab; set { _settings.ShowTodayTab=value; SettingsService.Save(_settings); OnPropertyChanged(); if(!value&&ActiveCardType==IslandCardType.Today) ActiveCardType=IslandCardType.Media; } }
    public bool ShowJapanHolidays { get => _settings.ShowJapanHolidays; set { _settings.ShowJapanHolidays = value; SettingsService.Save(_settings); OnPropertyChanged(); } }
    public bool DownloadAudioFormat { get => _settings.DownloadAudioFormat; set { if (_settings.DownloadAudioFormat == value) return; _settings.DownloadAudioFormat = value; SettingsService.Save(_settings); OnPropertyChanged(); } }
    public bool WeekStartsOnMonday { get => _settings.WeekStartsOnMonday; set { _settings.WeekStartsOnMonday = value; SettingsService.Save(_settings); OnPropertyChanged(); } }
    public bool ShowIndonesiaHolidays { get => _settings.ShowIndonesiaHolidays; set { _settings.ShowIndonesiaHolidays = value; SettingsService.Save(_settings); OnPropertyChanged(); } }
    public bool HideInFullscreen { get=>_settings.HideInFullscreen; set { _settings.HideInFullscreen=value; SettingsService.Save(_settings); OnPropertyChanged(); } }
    public bool EnablePowerAlerts { get=>_settings.EnablePowerAlerts; set { if(_settings.EnablePowerAlerts==value) return; _settings.EnablePowerAlerts=value; SettingsService.Save(_settings); OnPropertyChanged(); } }

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

    public bool EnableHardwareAlerts { get=>_settings.EnableHardwareAlerts; set { _settings.EnableHardwareAlerts=value; SettingsService.Save(_settings); OnPropertyChanged();_hardwareNoticeUntil=default;_hardwareAlerts.ResetContinuity();NotifyHardwareNotice(); } }
    private bool _displayAwake=true;
    public void StopHardwareCapture(){EndPlaytimeSession();EndGameProfile();_lol.Stop();StopPubgFetch();_hardwareService.SetGameCapture(false);_hardwareService.StopCpuTelemetry();}
    public void SetDisplayAwake(bool awake){Fx?.Configure(ShowFxRatesTab,_isWorkstationLocked||!awake);_deviceBatteries?.Configure(EnableDeviceBatteries,_isWorkstationLocked||!awake);if(!awake){_hardwareNoticeUntil=default;_hardwareAlerts.ResetContinuity();NotifyHardwareNotice();DismissPrayerArrival();}_displayAwake=awake;_hardwareService.ResetSamplingBaseline();if(!awake)_hardwareService.SetGameCapture(false);}
    public bool IsBatteryModuleActive => ActiveSystemModule=="Battery";
    public FxRatesService? Fx {get;private set;}
    public void ConfigureFx(FxRatesService service){Fx=service;OnPropertyChanged(nameof(Fx));}
    /// <summary>Read-only calendar feed behind the Today agenda (null until the app wires it up).</summary>
    public CalendarFeedService? CalendarFeed {get;private set;}
    public void ConfigureCalendar(CalendarFeedService service){CalendarFeed=service;OnPropertyChanged(nameof(CalendarFeed));ApplyReminderSettings();}
    public bool ShowFxRatesTab {get=>_settings.ShowFxRatesTab;set{_settings.ShowFxRatesTab=value;SettingsService.Save(_settings);OnPropertyChanged();Fx?.Configure(value,_isWorkstationLocked||!_displayAwake);if(!value&&ActiveCardType==IslandCardType.FxRates)ActiveCardType=IslandCardType.Media;}}
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
        set { _settings.EnableAiUsageAlerts = value; _aiUsageService.AlertsEnabled = value; SettingsService.Save(_settings); OnPropertyChanged(); }
    }

    public bool IsAiProviderEnabled(string id) =>
        !(_settings.HiddenAiProviders ?? new()).Contains(id, StringComparer.OrdinalIgnoreCase);

    public void SetAiProviderEnabled(string id, bool enabled)
    {
        var hidden = _settings.HiddenAiProviders ??= new();
        hidden.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        if (!enabled) hidden.Add(id);
        _aiUsageService.HiddenProviders = hidden;
        SettingsService.Save(_settings);
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
            _noticeIsDevice = false; _noticeIsWeather = false;
            _hardwareNoticeText = $"{reading.ProviderName} {reading.LeftPercent:0}% left";
            _hardwareNoticeUntil = DateTimeOffset.UtcNow.AddSeconds(8);
            _hardwareNoticeTimer.Stop(); _hardwareNoticeTimer.Start();
            NotifyHardwareNotice();
        });
    }

    public bool ShowAiUsageTab { get => _settings.ShowAiUsageTab; set { _settings.ShowAiUsageTab = value; SettingsService.Save(_settings); OnPropertyChanged(); if (!value && ActiveCardType == IslandCardType.AiUsage) ActiveCardType = IslandCardType.Media; } }
    public bool ShowScriptTab { get => _settings.ShowScriptTab; set { _settings.ShowScriptTab = value; SettingsService.Save(_settings); OnPropertyChanged(); if (!value && ActiveCardType == IslandCardType.Script) ActiveCardType = IslandCardType.Media; } }
    public bool ShowDownloadsTab { get => _settings.ShowDownloadsTab; set { _settings.ShowDownloadsTab = value; SettingsService.Save(_settings); OnPropertyChanged(); if (!value && ActiveCardType == IslandCardType.Downloads) ActiveCardType = IslandCardType.Media; } }

    public double? IslandHorizontalRatio { get => _settings.IslandHorizontalRatio; set { _settings.IslandHorizontalRatio = value; SettingsService.Save(_settings); OnPropertyChanged(); } }
    public bool ExcludeFromScreenCapture { get => _settings.ExcludeFromScreenCapture; set { _settings.ExcludeFromScreenCapture = value; SettingsService.Save(_settings); OnPropertyChanged(); ExcludeFromScreenCaptureChanged?.Invoke(value); } }
    public event Action<bool>? ExcludeFromScreenCaptureChanged;
    public int ScriptWpm { get => _settings.ScriptWpm; set { _settings.ScriptWpm = value; SettingsService.Save(_settings); OnPropertyChanged(); } }

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

    public PrayerSchedule? CurrentPrayerSchedule
    {
        get => _currentPrayerSchedule;
        set { _currentPrayerSchedule = value; OnPropertyChanged(); OnPropertyChanged(nameof(PrayerPillCountdownText)); OnPropertyChanged(nameof(IsPrayerInMainPill)); NotifyPrayerPresentation(); }
    }

    public string CurrentLyricLine
    {
        get => _currentLyricLine;
        set { if (_currentLyricLine != value) { _currentLyricLine = value; OnPropertyChanged(); OnPropertyChanged(nameof(CompactCapsuleText)); OnPropertyChanged(nameof(CompactLineSeconds)); } }
    }

    private double _currentLyricLineSeconds;

    /// <summary>
    /// Seconds the capsule's current lyric line stays on screen (until the next line), so a line too wide for the capsule scrolls
    /// fast enough to be read to its end (MarqueeTextBlock.PaceSeconds). 0 whenever the capsule shows anything but a lyric line.
    /// </summary>
    public double CompactLineSeconds =>
        !string.IsNullOrWhiteSpace(_currentLyricLine) && CompactCapsuleText == _currentLyricLine ? _currentLyricLineSeconds : 0;

    /// <summary>Time from <paramref name="position"/> to the next lyric entry (a blank pause marker counts), or to the end of the track for the last line.</summary>
    private double LyricLineSecondsFrom(int activeIdx, TimeSpan position)
    {
        double seconds = 6.0;
        if (_currentLyrics != null && activeIdx + 1 < _currentLyrics.Lines.Count)
            seconds = (_currentLyrics.Lines[activeIdx + 1].Timestamp - position).TotalSeconds;
        else if (_currentMedia.Duration.TotalSeconds > 0)
            seconds = (_currentMedia.Duration - position).TotalSeconds;
        return double.IsFinite(seconds) ? Math.Clamp(seconds, 1.0, 20.0) : 6.0;
    }

    private string _currentLyricRomaji = "";
    public string CurrentLyricRomaji
    {
        get => _currentLyricRomaji;
        set { if (_currentLyricRomaji != value) { _currentLyricRomaji = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCurrentLyricRomaji)); OnPropertyChanged(nameof(HasCompactLyricRomaji)); } }
    }

    public bool HasCurrentLyricRomaji => ShowLyricsPronunciation && !string.IsNullOrWhiteSpace(_currentLyricRomaji);
    public bool HasCompactLyricRomaji => !IsHardwareNoticeVisible && !IsPrayerReminderVisible && !IsPrayerArrivalVisible && HasActiveMedia && _currentMedia.IsPlaying && !_wasGaming && !_isStatsPinnedToCapsule && !IsNotificationInMainPill && !string.IsNullOrWhiteSpace(_currentLyricLine) && !string.IsNullOrWhiteSpace(_currentLyricRomaji);

    public ObservableCollection<RubyPiece> CurrentLyricPieces { get; } = new();
    public ObservableCollection<RubyPiece> PreviousLyricPieces { get; } = new();
    public ObservableCollection<RubyPiece> NextLyricPieces { get; } = new();
    public bool HasCurrentLyricRuby => CurrentLyricPieces.Count > 0;
    public bool HasPreviousLyricRuby => PreviousLyricPieces.Count > 0;
    public bool HasNextLyricRuby => NextLyricPieces.Count > 0;

    private string _previousLyricLine = "";
    public string PreviousLyricLine
    {
        get => _previousLyricLine;
        set { if (_previousLyricLine != value) { _previousLyricLine = value; OnPropertyChanged(); } }
    }

    private string _nextLyricLine = "";
    public string NextLyricLine
    {
        get => _nextLyricLine;
        set { if (_nextLyricLine != value) { _nextLyricLine = value; OnPropertyChanged(); } }
    }

    private bool _lyricsLoading,_lyricLookupFailed,_forceLyricsRefresh;
    public string PlainLyrics => _currentLyrics?.PlainLyrics??"";
    public string LyricsStatusText => !EnableSyncedLyrics?"Synced lyrics are off":_lyricsLoading?"Loading lyrics…":_currentLyrics?.IsSynced==true?"Synced":_currentLyrics?.IsInstrumental==true?"Instrumental":!string.IsNullOrWhiteSpace(PlainLyrics)?"Plain text":_lyricLookupFailed?"Lyrics unavailable — retry":"No lyrics found";
    private void RefreshLyricsStatus(){OnPropertyChanged(nameof(LyricsStatusText));OnPropertyChanged(nameof(PlainLyrics));OnPropertyChanged(nameof(HasLyrics));}
    public Task RetryLyricsAsync(){_forceLyricsRefresh=true;_lastFetchedTrack="";OnMediaUpdated(CurrentMedia);return Task.CompletedTask;}
    public bool HasLyrics => _currentLyrics != null && _currentLyrics.IsSynced;

    // ───────────── Feature tab visibility toggles ─────────────
    public bool ShowStatsTab
    {
        get => _settings?.ShowStatsTab ?? true;
        set
        {
            if (_settings != null && _settings.ShowStatsTab != value)
            {
                _settings.ShowStatsTab = value;
                SettingsService.Save(_settings);
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
                _settings.ShowWeatherTab = value;
                SettingsService.Save(_settings);
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
                _settings.ShowPomodoroTab = value;
                SettingsService.Save(_settings);
                OnPropertyChanged();
                if (!value && ActiveCardType == IslandCardType.Pomodoro) ActiveCardType = IslandCardType.Media;
            }
        }
    }

    public bool EnablePrayerTimes
    {
        get => _settings?.EnablePrayerTimes ?? true;
        set
        {
            if (_settings != null && _settings.EnablePrayerTimes != value)
            {
                _settings.EnablePrayerTimes = value;
                if(!value){PrayerAudio.Stop();_prayerCancellation?.Cancel();} else { _ = UpdatePrayerLocationAndScheduleAsync(); }
                SettingsService.Save(_settings);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPrayerInMainPill));
                NotifyPrayerPresentation();
                if (!value && ActiveCardType == IslandCardType.Prayer) ActiveCardType = IslandCardType.Media;
            }
        }
    }

    public bool EnablePrayerPreNotification
    {
        get => _settings?.EnablePrayerPreNotification ?? true;
        set
        {
            if (_settings != null && _settings.EnablePrayerPreNotification != value)
            {
                _settings.EnablePrayerPreNotification = value;
                SettingsService.Save(_settings);
                OnPropertyChanged();
                NotifyPrayerPresentation();
            }
        }
    }

    public string PrayerSoundMode
    {
        get => _settings?.PrayerSoundMode ?? "Tone";
        set
        {
            if (_settings != null && _settings.PrayerSoundMode != value)
            {
                _settings.PrayerSoundMode = MochiDock.Core.PrayerTime.NormalizeSound(value);
                PrayerAudio.Stop();
                SettingsService.Save(_settings);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPrayerSoundTone));
                OnPropertyChanged(nameof(IsPrayerSoundSilent));
            }
        }
    }

    public bool IsPrayerSoundTone => PrayerSoundMode == "Tone";
    public bool IsPrayerSoundSilent => PrayerSoundMode == "Silent";

    public bool ShowFileShelfTab
    {
        get => _settings?.ShowFileShelfTab ?? true;
        set
        {
            if (_settings != null && _settings.ShowFileShelfTab != value)
            {
                _settings.ShowFileShelfTab = value;
                SettingsService.Save(_settings);
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
                _settings.ShowClipboardTab = value;
                SettingsService.Save(_settings);
                OnPropertyChanged();
                if (!value && ActiveCardType == IslandCardType.Clipboard) ActiveCardType = IslandCardType.Media;
            }
        }
    }

    public bool EnableSyncedLyrics
    {
        get => _settings?.EnableSyncedLyrics ?? true;
        set
        {
            if (_settings == null || _settings.EnableSyncedLyrics == value) return;
            _settings.EnableSyncedLyrics = value;
            SettingsService.Save(_settings);
            OnPropertyChanged();
            // Off: drop what is showing and stop looking up. On: look the current track up again.
            _lyricCancellation?.Cancel();
            _lyricRequests.Next();
            _currentLyrics = null;
            _lyricsLoading = false;
            ClearVisibleLyrics();
            _lastFetchedTrack = "";
            RefreshLyricsStatus();
            if (value && _currentMedia != null) OnMediaUpdated(_currentMedia);
        }
    }

    public bool EnableSounds
    {
        get => _settings?.EnableSounds ?? true;
        set
        {
            if (_settings == null || _settings.EnableSounds == value) return;
            _settings.EnableSounds = value;
            SoundService.Enabled = value;
            SettingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    /// <summary>Re-reads settings.json (after flushing pending saves) and applies it the same way an import does.</summary>
    public async Task ReloadSettingsAsync()
    {
        await SettingsService.FlushAsync();
        ApplyImportedSettings(SettingsService.Load());
    }

    /// <summary>Lyric timing shift in ms; positive = lines appear earlier. Persisted, clamped to +/-5 s.</summary>
    public int LyricsOffsetMs
    {
        get => _settings?.LyricsOffsetMs ?? 0;
        set
        {
            int clamped = MochiDock.Core.LyricOffset.Clamp(value);
            if (_settings == null || _settings.LyricsOffsetMs == clamped) return;
            _settings.LyricsOffsetMs = clamped;
            SettingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(LyricsOffsetText));
            // The tick timer is stopped while paused, so re-evaluate the active line immediately.
            _displayedLyricIndex = -2;
            if (_currentMedia != null) OnTimelineUpdated(_currentMedia.Position, _currentMedia.Duration);
        }
    }
    public string LyricsOffsetText => MochiDock.Core.LyricOffset.Label(LyricsOffsetMs);
    public void NudgeLyricsOffset(int deltaMs) => LyricsOffsetMs += deltaMs;
    public void ResetLyricsOffset() => LyricsOffsetMs = 0;

    public bool ShowLyricsPronunciation
    {
        get => _settings?.ShowLyricsPronunciation ?? true;
        set
        {
            if (_settings != null && _settings.ShowLyricsPronunciation != value)
            {
                _settings.ShowLyricsPronunciation = value;
                SettingsService.Save(_settings);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCurrentLyricRomaji));
            }
        }
    }

    public bool ShowLyricsFurigana
    {
        get => _settings?.ShowLyricsFurigana ?? true;
        set
        {
            if (_settings != null && _settings.ShowLyricsFurigana != value)
            {
                _settings.ShowLyricsFurigana = value;
                SettingsService.Save(_settings);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCurrentLyricRuby));
            }
        }
    }

    private readonly MochiDock.Core.PrayerCountdownState _prayerCountdown=new();
    public bool IsPrayerReminderVisible => _settings.EnablePrayerTimes && _settings.EnablePrayerPreNotification && _settings.ShowPrayerInCapsule && !_isWorkstationLocked && !IsNotificationInMainPill && ShouldShowNotification("Prayer") && CurrentPrayerSchedule?.NextPrayerInstant is {} at && _prayerCountdown.Evaluate($"{CurrentPrayerSchedule.Latitude}|{CurrentPrayerSchedule.Longitude}|{CurrentPrayerSchedule.NextPrayer?.Name}|{at:O}",at,CurrentPrayerSchedule.NowUtc);
    public string PrayerReminderText => CurrentPrayerSchedule?.NextPrayerInstant is {} at ? $"{CurrentPrayerSchedule.NextPrayer?.Name} {MochiDock.Core.PrayerCountdownState.Minutes(at,CurrentPrayerSchedule.NowUtc)} min" : "";
    public void AcknowledgePrayerCountdown(){_prayerCountdown.Acknowledge();NotifyPrayerPresentation();}

    private DateTimeOffset _prayerArrivalUntil;
    private readonly DispatcherTimer _prayerArrivalTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private string _prayerArrivalTitle = "";
    private string _prayerArrivalSubtitle = "";

    public bool IsPrayerArrivalVisible =>
        _settings.EnablePrayerTimes && _settings.ShowPrayerInCapsule &&
        !_isWorkstationLocked && _displayAwake && !_wasGaming &&
        !IsNotificationInMainPill && ShouldShowNotification("Prayer") &&
        DateTimeOffset.UtcNow < _prayerArrivalUntil;

    public string PrayerArrivalTitle => _prayerArrivalTitle;
    public string PrayerArrivalSubtitle => _prayerArrivalSubtitle;

    public void ShowPrayerArrival(string prayerName, DateTimeOffset prayerAt, string city)
    {
        string displayName = prayerName.Equals("Asr", StringComparison.OrdinalIgnoreCase) ? "Ashar" : prayerName;
        _prayerArrivalTitle = $"It's time for {displayName}";
        string timePart = prayerAt.ToString("HH:mm");
        if (CurrentPrayerSchedule != null && !string.IsNullOrWhiteSpace(CurrentPrayerSchedule.TimeZoneId))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(CurrentPrayerSchedule.TimeZoneId);
                timePart = TimeZoneInfo.ConvertTime(prayerAt, zone).ToString("HH:mm");
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
        }
        string cityPart = !string.IsNullOrWhiteSpace(city) ? city : (CurrentPrayerSchedule?.ResolvedLocation ?? "");
        _prayerArrivalSubtitle = !string.IsNullOrWhiteSpace(cityPart) ? $"{timePart} · {cityPart}" : timePart;
        _prayerArrivalUntil = DateTimeOffset.UtcNow.AddSeconds(60);
        _prayerArrivalTimer?.Stop();
        _prayerArrivalTimer?.Start();
        NotifyPrayerArrival();
        ShowMoment(MascotMood.Praying, TimeSpan.FromSeconds(20));
    }

    public void DismissPrayerArrival()
    {
        _prayerArrivalUntil = default;
        _prayerArrivalTimer?.Stop();
        NotifyPrayerArrival();
    }

    private void NotifyPrayerArrival()
    {
        OnPropertyChanged(nameof(IsPrayerArrivalVisible));
        OnPropertyChanged(nameof(PrayerArrivalTitle));
        OnPropertyChanged(nameof(PrayerArrivalSubtitle));
        NotifyPrayerPresentation();
    }

    private void NotifyPrayerPresentation()
    {
        OnPropertyChanged(nameof(IsPrayerReminderVisible));
        OnPropertyChanged(nameof(PrayerReminderText));
        OnPropertyChanged(nameof(IsWeatherInPrayerReminder));
        OnPropertyChanged(nameof(IsWeatherInMainPill));
        OnPropertyChanged(nameof(IsPomodoroInMainPill));
        OnPropertyChanged(nameof(IsBothPomodoroAndWeatherInMainPill));
        OnPropertyChanged(nameof(HasCompactLyricRomaji));
        OnPropertyChanged(nameof(IsAudioWaveformVisible));
        OnPropertyChanged(nameof(CompactCapsuleText));
        OnPropertyChanged(nameof(IsCompactMarqueeEnabled));
        OnPropertyChanged(nameof(IsCompactTextVisible));
    }
    public bool IsPrayerInMainPill =>
        (_settings?.ShowPrayerInCapsule ?? true) &&
        _currentPrayerSchedule?.NextPrayer != null &&
        !HasActiveMedia && !IsStatsShownInCapsule && !IsNotificationInMainPill && !IsPomodoroInMainPill;

    public string PrayerPillCountdownText =>
        _currentPrayerSchedule?.NextPrayer != null ? $"🕌 {_currentPrayerSchedule.NextPrayer.Name} in {_currentPrayerSchedule.CountdownText}" : "";

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
                _settings.IsStatsPinnedToCapsule = value;
                SettingsService.Save(_settings);

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

    // ───────────── Multi-Notification Rotation & Priority Queue ─────────────
    private readonly List<IslandCardType> _notificationQueue = new();
    private int _rotationIndex = 0;
    private bool _isNotificationTakeover = false;
    private DateTime _takeoverStartTime = DateTime.MinValue;

    public bool IsNotificationTakeover => _isNotificationTakeover;

    public void StartNotificationTakeover()
    {
        _isNotificationTakeover = true;
        _takeoverStartTime = DateTime.UtcNow;
        RaiseCapsuleStateChanged();
    }

    public void EndNotificationTakeover()
    {
        if (!_isNotificationTakeover) return;
        _isNotificationTakeover = false;
        RaiseCapsuleStateChanged();
    }

    // A message that is muted, snoozed or arrives in quiet hours is still stored (the tab shows it), but it must not enter the notification
    // queue: the queue is what puts "New message" in the capsule and starts the take-over.
    private bool _recordSilently;

    private void SetCurrentSilently(bool silent, Action assign)
    {
        _recordSilently = silent;
        try { assign(); }
        finally { _recordSilently = false; }
    }

    public void EnqueueNotification(IslandCardType type)
    {
        _notificationQueue.Remove(type);
        _notificationQueue.Insert(0, type); // Priority: newest incoming is front
        _rotationIndex = 0;

        bool isGaming = _hardwareService.IsGameRunning() || _wasGaming;
        if (HasActiveMedia && !isGaming)
        {
            StartNotificationTakeover();
        }
        else
        {
            RaiseCapsuleStateChanged();
        }
    }

    public void RemoveFromNotificationQueue(IslandCardType type)
    {
        _notificationQueue.Remove(type);
        if (_rotationIndex >= _notificationQueue.Count)
        {
            _rotationIndex = Math.Max(0, _notificationQueue.Count - 1);
        }
        if (_notificationQueue.Count == 0)
        {
            _isNotificationTakeover = false;
        }
        RaiseCapsuleStateChanged();
    }

    public void AdvanceNotificationRotation()
    {
        if (_notificationQueue.Count <= 1) return;
        _rotationIndex = (_rotationIndex + 1) % _notificationQueue.Count;
        RaiseCapsuleStateChanged();
    }

    public IslandCardType? CurrentRotationItem =>
        _notificationQueue.Count > 0 && _rotationIndex < _notificationQueue.Count
            ? _notificationQueue[_rotationIndex]
            : null;

    public int NotificationCount => _notificationQueue.Count;

    public bool IsNotificationInMainPill =>
        _notificationQueue.Count > 0 && (_isNotificationTakeover || (!HasActiveMedia && !IsStatsShownInCapsule));

    public string RotationPositionText =>
        _notificationQueue.Count > 1 ? $"{_rotationIndex + 1}/{_notificationQueue.Count}" : "";

    public bool ShowRotationPosition =>
        IsNotificationInMainPill && _notificationQueue.Count > 1;

    public void RaiseCapsuleStateChanged()
    {
        OnPropertyChanged(nameof(CompactLineSeconds));
        OnPropertyChanged(nameof(HasActiveWhatsApp));
        OnPropertyChanged(nameof(HasActiveDiscord));
        OnPropertyChanged(nameof(HasActiveApps));
        OnPropertyChanged(nameof(HasActiveEmail));
        OnPropertyChanged(nameof(HasActiveMedia));
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

    public bool HasCompanionBubble =>
        _notificationQueue.Count > 0 && (HasActiveMedia || IsStatsShownInCapsule) && !_isNotificationTakeover && !_wasGaming && !_hardwareStats.IsGamingMode;

    public bool HasMultipleActivities =>
        ((HasActiveMedia ? 1 : 0) + _notificationQueue.Count + (IsStatsShownInCapsule ? 1 : 0)) > 1;

    public bool HasSingleNotification => IsNotificationInMainPill;

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

    public IslandCardType? PrimaryCompanionItem =>
        _notificationQueue.Count > 0 ? _notificationQueue[0] : null;

    public string CompanionBubbleType => PrimaryCompanionItem switch
    {
        IslandCardType.WhatsApp => "WhatsApp",
        IslandCardType.Discord => "Discord",
        IslandCardType.Apps => "Apps",
        IslandCardType.Email => "Email",
        _ => ""
    };

    public string CompanionBubbleBadge => PrimaryCompanionItem switch
    {
        IslandCardType.WhatsApp => "💬",
        IslandCardType.Discord => "🎮",
        IslandCardType.Apps => "🔔",
        IslandCardType.Email => "✉️",
        _ => ""
    };

    public string CompanionBubbleColor => PrimaryCompanionItem switch
    {
        IslandCardType.WhatsApp => "#25D366",
        IslandCardType.Discord => "#5865F2",
        IslandCardType.Apps => "#8E8E98",
        IslandCardType.Email => "#0A84FF",
        _ => "#8E8E98"
    };

    public string CompanionExtraCountText =>
        _notificationQueue.Count > 1 ? $"+{_notificationQueue.Count - 1}" : "";

    public bool HasCompanionExtraCount => _notificationQueue.Count > 1;
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
            _baseMood, _settings?.ExpressiveMochi ?? true, _mascotOverdue, _mascotGpuHot, _mascotOverloaded, _mascotBatteryLow, _mascotWeather, _mascotPetted, _isWorkstationLocked || !_displayAwake, _mascotActivity));
        if (_mascotMood == mood) return;
        _mascotMood = mood;
        OnPropertyChanged(nameof(MascotMood));
    }

    public MediaInfo CurrentMedia
    {
        get => _currentMedia;
        set
        {
            _currentMedia = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HeaderBadgeText));
            RaiseCapsuleStateChanged();
        }
    }

    public EmailNotification? CurrentEmail
    {
        get => _currentEmail;
        set
        {
            _currentEmail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MarkReadTooltip));
            OnPropertyChanged(nameof(CanMarkCurrentEmailRead));
            if (value != null) { if (!_recordSilently) EnqueueNotification(IslandCardType.Email); }
            else RemoveFromNotificationQueue(IslandCardType.Email);
        }
    }

    /// <summary>
    /// Mark Read is shown only when it really marks the email read at its source: not for Mailspring (its database is opened read
    /// only) or a mail account without "Mark as read on the server". Dismiss still removes those from the island.
    /// </summary>
    public bool CanMarkCurrentEmailRead => CurrentEmail is { } email && _mailService.CanMarkAsRead(email);

    public string MarkReadTooltip =>
        string.Equals(CurrentEmail?.ClientSource, "Webmail", StringComparison.OrdinalIgnoreCase)
            ? "Mark this email as read on the mail server"
            : $"Mark this email as read in {CurrentEmail?.ClientSource ?? "Outlook"}";

    public WhatsAppNotification? CurrentWhatsApp
    {
        get => _currentWhatsApp;
        set
        {
            _currentWhatsApp = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanAllowCurrentWhatsAppGroup));
            if (value != null) { if (!_recordSilently) EnqueueNotification(IslandCardType.WhatsApp); }
            else RemoveFromNotificationQueue(IslandCardType.WhatsApp);
        }
    }

    public DiscordNotification? CurrentDiscord
    {
        get => _currentDiscord;
        set
        {
            _currentDiscord = value;
            OnPropertyChanged();
            if (value != null) { if (!_recordSilently) EnqueueNotification(IslandCardType.Discord); }
            else RemoveFromNotificationQueue(IslandCardType.Discord);
        }
    }

    public string ReplyText
    {
        get => _replyText;
        set
        {
            if (_replyText != value)
            {
                _replyText = value;
                OnPropertyChanged();
            }
        }
    }

    private void OnMediaUpdated(MediaInfo info)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            CurrentMedia = info;
            OnPropertyChanged(nameof(IsAudioWaveformVisible));
            string trackIdentity = LrclibService.CreateIdentity(info.SessionIdentity, info.Title, info.Artist, info.Album, info.Duration.TotalSeconds).Key;
            if (!trackIdentity.Equals(_lastFetchedTrack, StringComparison.Ordinal))
            {
                var forceLyrics=_forceLyricsRefresh;_forceLyricsRefresh=false;_lyricsLoading=true;_lyricLookupFailed=false;
                _lastFetchedTrack = trackIdentity;
                _lyricCancellation?.Cancel();
                _lyricCancellation?.Dispose();
                _lyricCancellation = new CancellationTokenSource();
                var lyricToken = _lyricCancellation.Token;
                long lyricGeneration = _lyricRequests.Next();
                _currentLyrics = null;RefreshLyricsStatus();
                CurrentLyricLine = "";
                CurrentLyricRomaji = "";
                PreviousLyricLine = "";
                NextLyricLine = "";
                CurrentLyricPieces.Clear();
                PreviousLyricPieces.Clear();
                NextLyricPieces.Clear();
                if (!EnableSyncedLyrics) { _lyricsLoading = false; RefreshLyricsStatus(); return; }
                _ = Task.Run(async () =>
                {
                    try {
                        var displayWatch = System.Diagnostics.Stopwatch.StartNew();
                        var lyrics = await _lrclibService.FetchLyricsAsync(info.Title, info.Artist, album: info.Album, durationSeconds: info.Duration.TotalSeconds, cancellationToken: lyricToken, sessionIdentity: info.SessionIdentity, forceRefresh: forceLyrics);
                        App.Current.Dispatcher.Invoke(() => {
                            if (lyricToken.IsCancellationRequested || !_lyricRequests.IsCurrent(lyricGeneration)) return;
                            _lyricsLoading=false;_lyricLookupFailed=_lrclibService.LastLookupFailed;
                            _currentLyrics = lyrics;RefreshLyricsStatus();
                            OnTimelineUpdated(CurrentMedia.Position,CurrentMedia.Duration);
                            System.Diagnostics.Debug.WriteLine($"Lyrics first display: {displayWatch.ElapsedMilliseconds}ms");
                            OnPropertyChanged(nameof(HasLyrics));
                        });
                        if(lyrics != null && !lyricToken.IsCancellationRequested) {
                            var snapshot = new SongLyrics { TrackName=lyrics.TrackName, ArtistName=lyrics.ArtistName, IsSynced=lyrics.IsSynced, PlainLyrics=lyrics.PlainLyrics, Lines=lyrics.Lines.Select(l=>new LyricLine { Text=l.Text, Timestamp=l.Timestamp }).ToList() };
                            var watch = System.Diagnostics.Stopwatch.StartNew();
                            await LyricRomanizerService.PronounceSongAsync(snapshot, lyricToken);
                            System.Diagnostics.Debug.WriteLine($"Lyrics pronunciation: {watch.ElapsedMilliseconds}ms");
                            await App.Current.Dispatcher.InvokeAsync(()=> {
                                if(lyricToken.IsCancellationRequested || !_lyricRequests.IsCurrent(lyricGeneration)) return;
                                _currentLyrics=snapshot;
                                OnTimelineUpdated(CurrentMedia.Position,CurrentMedia.Duration);
                                OnPropertyChanged(nameof(HasLyrics));
                            });
                        }
                    } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
                });
            }
            if (info.IsPlaying)
            {
                if (!_wasGaming)
                {
                    MascotMood = MascotMood.Music;
                }
                if (info.AlbumArt != null)
                {
                    var color = ColorExtractor.ExtractDominantColor(info.AlbumArt);
                    AmbientGlowColor = System.Windows.Media.Color.FromArgb(160, color.R, color.G, color.B);
                }
                else
                {
                    AmbientGlowColor = System.Windows.Media.Color.FromArgb(140, 29, 185, 84); // Spotify green
                }
            }
            else
            {
                AmbientGlowColor = System.Windows.Media.Color.FromArgb(120, 10, 132, 255);
                if (!_wasGaming)
                {
                    MascotMood = MascotMood.Idle;
                }
                // When paused, clear the romaji reading line so pill collapses cleanly
                CurrentLyricRomaji = "";
            }

            OnPropertyChanged(nameof(HasCompactLyricRomaji));
            OnPropertyChanged(nameof(HasCurrentLyricRomaji));
            OnPropertyChanged(nameof(CompactCapsuleText));
        });
    }

    private void OnEmailReceived(EmailNotification notif)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            // Maintain multi-email history (most recent first, capped at 10)
            RecentEmailMessages.Insert(0, notif);
            while (RecentEmailMessages.Count > 10)
            {
                RecentEmailMessages.RemoveAt(RecentEmailMessages.Count - 1);
            }

            bool emailShown = ShouldShowNotification("Email");
            SetCurrentSilently(!emailShown, () => CurrentEmail = notif);
            if (!emailShown) { MochiDock.Services.AppLog.Warn("Email", "Not shown: email notifications are muted, snoozed or in quiet hours."); return; }
            bool isGaming = _hardwareService.IsGameRunning();

            if (_isWorkstationLocked && _settings.EnableOvernightGreeting)
            {
                // Locked screen: queue silently
                MochiDock.Services.AppLog.Warn("Email", "Queued: the workstation is locked.");
                _lockedEmailCount++;
                _lockedPreviews.Add($"✉️ {notif.Sender}: {notif.Subject}");
                return;
            }

            if (isGaming && GamingDndActive)
            {
                // Gaming DND: queue silently without audio or expansion
                MochiDock.Services.AppLog.Warn("Email", "Queued: a game is running (gaming do-not-disturb).");
                _queuedGamingEmailCount++;
                _queuedGamingPreviews.Add($"✉️ {notif.Sender}: {notif.Subject}");
                return;
            }

            // Normal Desktop state: play sound and show notification
            SnapshotCardBeforeNotification();
            SoundService.PlayNotificationSound();
            ActiveCardType = IslandCardType.Email;
            MascotMood = MascotMood.Excited;

            if (_settings.AutoExpandNotificationsOnArrival)
            {
                IsExpanded = true;
                _autoCollapseTimer.Stop();
                _autoCollapseTimer.Start();
            }
            else
            {
                // Minimal notification mode: a capsule banner that expands on hover
                StartNotificationTakeover();
            }
        });
    }

    /// <summary>Applies the Outlook folders to watch (besides every Inbox) to the running mail service.</summary>
    public void ApplyOutlookFolders(IReadOnlyList<string> names) =>
        (_mailService as CompositeMailService)?.Find<OutlookMailService>()?.UpdateTrackedFolders(names, _settings.OutlookOnlyListedFolders);

    private bool ShouldShowNotification(string app)
    {
        var now = DateTime.Now;
        if (now < _snoozedUntil) return false;
        if (app == "Email" && _settings.MuteEmail) return false;
        if (app == "Discord" && _settings.MuteDiscord) return false;
        if (app == "Apps" && _settings.MuteApps) return false;
        if (app == "WhatsApp" && _settings.MuteWhatsApp) return false;

        if (_settings.QuietHoursEnabled &&
            MochiDock.Core.SettingsValidation.IsQuietTime(now.TimeOfDay, _settings.QuietHoursStart, _settings.QuietHoursEnd))
            return false;
        return true;
    }

    public void SnoozeNotifications(TimeSpan duration)
    {
        _snoozedUntil = DateTime.Now.Add(duration);
        _settings.SnoozeUntilTicks = _snoozedUntil.Ticks;
        SettingsService.Save(_settings);
    }

    private readonly MochiDock.Core.HardwareAlertPolicy _hardwareAlerts=new();
    private readonly DispatcherTimer _hardwareNoticeTimer=new(){Interval=TimeSpan.FromSeconds(8)};
    public void StopPresentationTimers(){_aiUsageService.Dispose();_hardwareNoticeTimer.Stop();_prayerArrivalTimer.Stop();_autoCollapseTimer.Stop();_hardwarePollTimer.Stop();}
    private DateTimeOffset _hardwareNoticeUntil;
    private string _hardwareNoticeText="";
    private bool _hardwarePresentationSuppressed;
    public void SetHardwarePresentationSuppressed(bool suppressed){_hardwarePresentationSuppressed=suppressed;if(suppressed){_hardwareNoticeUntil=default;NotifyHardwareNotice();}}
    public bool IsAutoCollapseDeferred {get;set;}
    public Func<bool>? IsDockInteractionActive {get;set;}
    public bool IsHardwareNoticeVisible => !_hardwarePresentationSuppressed && (EnableHardwareAlerts || _noticeIsAi || _noticeIsAgenda || _noticeIsGame || _noticeIsDevice || _noticeIsWeather) && !_isWorkstationLocked && _displayAwake && !_wasGaming && !IsNotificationInMainPill && !IsPrayerReminderVisible && ShouldShowNotification("Hardware") && DateTimeOffset.UtcNow<_hardwareNoticeUntil;
    public string HardwareNoticeText => _hardwareNoticeText;
    private void NotifyHardwareNotice(){OnPropertyChanged(nameof(IsHardwareNoticeVisible));OnPropertyChanged(nameof(HardwareNoticeText));OnPropertyChanged(nameof(CompactCapsuleText));OnPropertyChanged(nameof(HasCompactLyricRomaji));OnPropertyChanged(nameof(IsAudioWaveformVisible));}
    public void OpenHardwareNotice(){bool ai=_noticeIsAi,agenda=_noticeIsAgenda,device=_noticeIsDevice,weather=_noticeIsWeather;_hardwareNoticeUntil=default;_noticeIsAi=false;_noticeIsAgenda=false;_noticeIsGame=false;_noticeIsDevice=false;_noticeIsWeather=false;NotifyHardwareNotice();if(agenda){AcknowledgeShownReminders();ShowToday(false);}else if(weather)ShowWeather(false);else if(ai)ShowAiUsage(false);else{if(device)SelectSystemModule("Battery");ShowHardware(false);}}
    private void CheckHardwareAlerts()
    {
        var now=DateTimeOffset.UtcNow;
        if(!EnableHardwareAlerts||_isWorkstationLocked||!_displayAwake){if((!_noticeIsAi&&!_noticeIsAgenda&&!_noticeIsGame&&!_noticeIsDevice&&!_noticeIsWeather)||_isWorkstationLocked||!_displayAwake){_hardwareNoticeUntil=default;_noticeIsAi=false;_noticeIsAgenda=false;_noticeIsGame=false;_noticeIsDevice=false;_noticeIsWeather=false;}_hardwareAlerts.ResetContinuity();NotifyHardwareNotice();return;}
        var s=HardwareStats;
        foreach(var metric in new[]{("CPU usage",s.CpuUsageReading,_settings.CpuAlertThreshold,"%"),("GPU usage",s.GpuUsageReading,_settings.GpuAlertThreshold,"%"),("CPU temperature",s.CpuTemperatureReading,_settings.TemperatureAlertThreshold,"°C"),("GPU temperature",s.GpuTemperatureReading,_settings.TemperatureAlertThreshold,"°C")}) {
            if(!_hardwareAlerts.Evaluate(metric.Item1,metric.Item2,metric.Item3,now))continue;
            if(_hardwarePresentationSuppressed||_wasGaming||IsNotificationInMainPill||IsPrayerReminderVisible||!ShouldShowNotification("Hardware"))continue;
            _noticeIsAi=false;_noticeIsAgenda=false;_noticeIsGame=false;_noticeIsDevice=false;_noticeIsWeather=false;_hardwareNoticeText=$"{metric.Item1} {metric.Item2!.Value:0}{metric.Item4}";_hardwareNoticeUntil=now.AddSeconds(8);_hardwareNoticeTimer.Stop();_hardwareNoticeTimer.Start();
        }
        if(_hardwarePresentationSuppressed||IsNotificationInMainPill||IsPrayerReminderVisible||!ShouldShowNotification("Hardware"))_hardwareNoticeUntil=default;
        NotifyHardwareNotice();
    }

    public void SetCapsuleScrollSpeed(double speed)
    {
        CapsuleScrollSpeed = Math.Clamp(speed, 10, 100);
        _settings.CapsuleScrollSpeed = CapsuleScrollSpeed;
        SettingsService.Save(_settings);
    }

    public void ApplyCapsuleTheme(string theme)
    {
        theme = MochiDock.Core.ThemePolicy.Normalize(theme);
        CapsuleTheme = theme;
        _settings.CapsuleTheme = theme;
        SettingsService.Save(_settings);

        if (System.Windows.Application.Current?.MainWindow != null)
        {
            ThemeService.ApplyTheme(System.Windows.Application.Current.MainWindow, theme);
        }
        App.ApplySettingsWindowTheme(theme);
    }

    private void OnGowaMessageReceived(WhatsAppNotification notif)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            // Maintain multi-message history (most recent first, capped at 10)
            RecentWhatsAppMessages.Insert(0, notif);
            while (RecentWhatsAppMessages.Count > 10)
            {
                RecentWhatsAppMessages.RemoveAt(RecentWhatsAppMessages.Count - 1);
            }

            // Per-Conversation Threading
            string chatKey = !string.IsNullOrEmpty(notif.FromJid) ? notif.FromJid : (notif.IsGroup ? notif.GroupName : notif.SenderName);
            var conv = ActiveWhatsAppConversations.FirstOrDefault(c => string.Equals(c.ChatJid, chatKey, StringComparison.OrdinalIgnoreCase) &&
                MochiDock.Core.GowaAccountPolicy.AppliesTo(c.DeviceId, notif.DeviceId));
            if (conv == null)
            {
                conv = new WhatsAppConversation
                {
                    ChatJid = chatKey,
                    DeviceId = notif.DeviceId,
                    Title = notif.IsGroup && !string.IsNullOrWhiteSpace(notif.GroupName) ? notif.GroupName : notif.SenderName,
                    IsGroup = notif.IsGroup,
                    UnreadCount = 0
                };
                ActiveWhatsAppConversations.Add(conv);

                // Fetch recent group history asynchronously on first message if group
                if (notif.IsGroup && !string.IsNullOrEmpty(notif.FromJid))
                {
                    _ = LoadGroupChatHistoryAsync(conv, notif.FromJid, notif.DeviceId);
                }
            }

            conv.UnreadCount++;
            conv.LatestMessage = notif;
            conv.Messages.Insert(0, notif);
            while (conv.Messages.Count > 10)
            {
                conv.Messages.RemoveAt(conv.Messages.Count - 1);
            }

            bool whatsAppShown = ShouldShowNotification("WhatsApp");
            SetCurrentSilently(!whatsAppShown, () => { SelectedWhatsAppConversation = conv; CurrentWhatsApp = notif; });
            ReplyText = "";
            if (!whatsAppShown) return;
            bool isGaming = _hardwareService.IsGameRunning();

            if (_isWorkstationLocked && _settings.EnableOvernightGreeting)
            {
                // Locked screen: queue silently
                _lockedWhatsAppCount++;
                _lockedPreviews.Add($"💬 {notif.Sender}: {notif.Message}");
                return;
            }

            if (isGaming && GamingDndActive && MochiDock.Core.GamingAllowList.AllowsWhatsApp(_settings.GamingAllowList, notif.FromJid, notif.DeviceId))
            {
                // On the "let through while gaming" list: show it in the gaming capsule, nothing else changes
                ShowGamingPassThrough(MochiDock.Core.GamingAllowList.CapsuleText(notif.IsGroup && !string.IsNullOrWhiteSpace(notif.GroupName) ? notif.GroupName : notif.Sender, notif.Message));
                return;
            }

            if (isGaming && GamingDndActive)
            {
                // Gaming DND: queue silently without audio or expansion
                _queuedGamingWhatsAppCount++;
                _queuedGamingPreviews.Add($"💬 {notif.Sender}: {notif.Message}");
                return;
            }

            // Normal Desktop state: play sound and show notification
            SnapshotCardBeforeNotification();
            SoundService.PlayNotificationSound();
            ActiveCardType = IslandCardType.WhatsApp;
            MascotMood = MascotMood.Excited;

            if (_settings.AutoExpandNotificationsOnArrival)
            {
                IsExpanded = true;
                _autoCollapseTimer.Stop();
                _autoCollapseTimer.Start();
            }
            else
            {
                // Minimal notification mode: a capsule banner that expands on hover
                StartNotificationTakeover();
            }
        });
    }

    private async Task LoadGroupChatHistoryAsync(WhatsAppConversation conv, string chatJid, string deviceId)
    {
        try
        {
            var history = await _gowaService.FetchRecentChatHistoryAsync(chatJid, 10, deviceId).ConfigureAwait(false);
            if (history != null && history.Count > 0)
            {
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Merge older messages from history not already in conv.Messages
                    var existingIds = new HashSet<string>(conv.Messages.Select(m => m.RemoteId).Where(id => !string.IsNullOrEmpty(id)));
                    foreach (var histMsg in history)
                    {
                        if (string.IsNullOrEmpty(histMsg.RemoteId) || !existingIds.Contains(histMsg.RemoteId))
                        {
                            conv.Messages.Add(histMsg);
                            existingIds.Add(histMsg.RemoteId);
                        }
                    }
                });
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
    }

    private void OnGowaMessageReadOrDismissed(string chatJid, List<string>? readIds, string deviceId)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            // Normalize targets: strip @s.whatsapp.net, @c.us, @lid, @g.us
            string cleanTarget = chatJid
                .Replace("@s.whatsapp.net", "")
                .Replace("@c.us", "")
                .Replace("@lid", "")
                .Replace("@g.us", "")
                .Trim();

            if (readIds != null && readIds.Count > 0)
            {
                var readSet = new HashSet<string>(readIds, StringComparer.OrdinalIgnoreCase);
                for (int i = RecentWhatsAppMessages.Count - 1; i >= 0; i--)
                {
                    var msg = RecentWhatsAppMessages[i];
                    if (!string.IsNullOrEmpty(msg.RemoteId) && readSet.Contains(msg.RemoteId))
                    {
                        RecentWhatsAppMessages.RemoveAt(i);
                    }
                }
            }
            else
            {
                // Full chat read or user replied to contact: purge all messages from this sender/chat
                for (int i = RecentWhatsAppMessages.Count - 1; i >= 0; i--)
                {
                    var msg = RecentWhatsAppMessages[i];
                    string msgClean = msg.FromJid
                        .Replace("@s.whatsapp.net", "")
                        .Replace("@c.us", "")
                        .Replace("@lid", "")
                        .Replace("@g.us", "")
                        .Trim();

                    // A chat read or answered on one account must not clear the same contact's messages on another account.
                    if (MochiDock.Core.GowaAccountPolicy.AppliesTo(msg.DeviceId, deviceId) &&
                        (string.Equals(msg.FromJid, chatJid, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(cleanTarget) && (msgClean.Contains(cleanTarget) || cleanTarget.Contains(msgClean)))))
                    {
                        RecentWhatsAppMessages.RemoveAt(i);
                    }
                }
            }

            // Also clean up ActiveWhatsAppConversations
            for (int i = ActiveWhatsAppConversations.Count - 1; i >= 0; i--)
            {
                var c = ActiveWhatsAppConversations[i];
                string cClean = c.ChatJid
                    .Replace("@s.whatsapp.net", "")
                    .Replace("@c.us", "")
                    .Replace("@lid", "")
                    .Replace("@g.us", "")
                    .Trim();

                if (MochiDock.Core.GowaAccountPolicy.AppliesTo(c.DeviceId, deviceId) &&
                    (string.Equals(c.ChatJid, chatJid, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(cleanTarget) && (cClean.Contains(cleanTarget) || cleanTarget.Contains(cClean)))) &&
                    !RecentWhatsAppMessages.Any(m => string.Equals(m.FromJid, c.ChatJid, StringComparison.OrdinalIgnoreCase) &&
                        MochiDock.Core.GowaAccountPolicy.AppliesTo(m.DeviceId, c.DeviceId)))
                {
                    ActiveWhatsAppConversations.RemoveAt(i);
                }
            }

            if (SelectedWhatsAppConversation != null && !ActiveWhatsAppConversations.Contains(SelectedWhatsAppConversation))
            {
                SelectedWhatsAppConversation = ActiveWhatsAppConversations.FirstOrDefault();
            }

            if (RecentWhatsAppMessages.Count > 0)
            {
                CurrentWhatsApp = RecentWhatsAppMessages[0];
            }
            else
            {
                // When all messages are cleared/read, dismiss the card automatically
                DismissWhatsApp();
                if (!_isPinned)
                {
                    IsExpanded = false;
                }
            }
        });
    }

    private void OnDiscordMessageReceived(DiscordNotification notif)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            // Maintain multi-message history (most recent first, capped at 10)
            RecentDiscordMessages.Insert(0, notif);
            while (RecentDiscordMessages.Count > 10)
            {
                RecentDiscordMessages.RemoveAt(RecentDiscordMessages.Count - 1);
            }

            bool discordShown = ShouldShowNotification("Discord");
            SetCurrentSilently(!discordShown, () => CurrentDiscord = notif);
            if (!discordShown) return;
            bool isGaming = _hardwareService.IsGameRunning();

            if (_isWorkstationLocked && _settings.EnableOvernightGreeting)
            {
                // Locked screen: queue silently
                _lockedDiscordCount++;
                _lockedPreviews.Add($"🎮 {notif.Sender}: {notif.Message}");
                return;
            }

            if (isGaming && GamingDndActive && MochiDock.Core.GamingAllowList.AllowsDiscord(_settings.GamingAllowList, notif.Sender))
            {
                ShowGamingPassThrough(MochiDock.Core.GamingAllowList.CapsuleText(notif.Sender, notif.Message));
                return;
            }

            if (isGaming && GamingDndActive)
            {
                // Gaming DND: queue silently without audio or expansion
                _queuedGamingDiscordCount++;
                _queuedGamingPreviews.Add($"🎮 {notif.Sender}: {notif.Message}");
                return;
            }

            // Normal Desktop state: play sound and show notification
            SnapshotCardBeforeNotification();
            SoundService.PlayNotificationSound();
            ActiveCardType = IslandCardType.Discord;
            MascotMood = MascotMood.Excited;

            if (_settings.AutoExpandNotificationsOnArrival)
            {
                IsExpanded = true;
                _autoCollapseTimer.Stop();
                _autoCollapseTimer.Start();
            }
            else
            {
                // Minimal notification mode: a capsule banner that expands on hover
                StartNotificationTakeover();
            }
        });
    }

    public void ToggleHardwareMode()
    {
        _hardwareService.IsGamingMode = !_hardwareService.IsGamingMode;
        _settings.IsHardwareGamingMode = _hardwareService.IsGamingMode;
        SettingsService.Save(_settings);

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

    private SongLyrics? _displayedLyricSnapshot;
    private int _displayedLyricIndex=-2;
    // Display guard (also covers lyrics pronounced and cached before the policy existed): a line with no kana, Han or Hangul, or whose reading
    // only repeats it, shows no reading and no ruby, so an English or romaji line never appears twice.
    private static string ReadingFor(LyricLine line)
    {
        string text = line.Text ?? "", reading = line.RomajiText ?? "";
        return !MochiDock.Core.LyricPronunciationPolicy.NeedsReading(text) || MochiDock.Core.LyricPronunciationPolicy.IsRedundant(text, reading) ? "" : reading;
    }

    private static IEnumerable<RubyPiece> PiecesFor(LyricLine line)
    {
        bool plain = !MochiDock.Core.LyricPronunciationPolicy.NeedsReading(line.Text);
        if (!plain && line.RubyPieces is { Count: > 0 } pieces) return pieces;
        return new[] { new RubyPiece { Text = line.Text ?? "", Romaji = plain ? "" : ReadingFor(line) } };
    }

    private void ClearVisibleLyrics(){_displayedLyricSnapshot=null;_displayedLyricIndex=-2;CurrentLyricLine="";CurrentLyricRomaji="";PreviousLyricLine="";NextLyricLine="";if(CurrentLyricPieces.Count>0)CurrentLyricPieces.Clear();if(PreviousLyricPieces.Count>0)PreviousLyricPieces.Clear();if(NextLyricPieces.Count>0)NextLyricPieces.Clear();OnPropertyChanged(nameof(HasCurrentLyricRuby));OnPropertyChanged(nameof(HasPreviousLyricRuby));OnPropertyChanged(nameof(HasNextLyricRuby));}
    private void OnTimelineUpdated(TimeSpan pos, TimeSpan dur)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            if (_currentMedia != null)
            {
                _currentMedia.Position = pos;
                _currentMedia.Duration = dur;
                OnPropertyChanged(nameof(CurrentMedia));
                if (_currentLyrics != null && _currentLyrics.IsSynced && _currentLyrics.Lines.Count > 0)
                {
                    // The playback clock is untouched (Spotify keeps its timestamp-corrected position); only the lookup position is shifted by the user's lyric offset.
                    var leadPos = TimeSpan.FromSeconds(MochiDock.Core.LyricOffset.Apply(pos.TotalSeconds, LyricsOffsetMs));
                    int activeIdx = MochiDock.Core.MediaPresentation.ActiveLine(_currentLyrics.Lines,static line=>line.Timestamp.TotalSeconds,leadPos.TotalSeconds);
                    if(activeIdx<0 || string.IsNullOrWhiteSpace(_currentLyrics.Lines[activeIdx].Text)){ClearVisibleLyrics();return;}
                    if(ReferenceEquals(_displayedLyricSnapshot,_currentLyrics)&&_displayedLyricIndex==activeIdx)return;
                    _displayedLyricSnapshot=_currentLyrics;_displayedLyricIndex=activeIdx;
                    if (activeIdx >= 0)
                    {
                        var activeLine = _currentLyrics.Lines[activeIdx];

                        // If the active line is an empty pause marker
                        if (string.IsNullOrWhiteSpace(activeLine.Text))
                        {
                            // A pause of 3 s or more before the next line clears the current lyric.
                            double gapToNext = 10.0;
                            if (activeIdx + 1 < _currentLyrics.Lines.Count)
                            {
                                gapToNext = (_currentLyrics.Lines[activeIdx + 1].Timestamp - leadPos).TotalSeconds;
                            }
                            if (gapToNext >= 3.0)
                            {
                                CurrentLyricLine = "";
                                CurrentLyricRomaji = "";
                                CurrentLyricPieces.Clear();
                                OnPropertyChanged(nameof(HasCurrentLyricRuby));
                            }
                            return;
                        }

                        // Pronunciation is prepared in the background lookup, never on the timeline dispatcher.

                        _currentLyricLineSeconds = LyricLineSecondsFrom(activeIdx, leadPos);
                        CurrentLyricLine = activeLine.Text;
                        OnPropertyChanged(nameof(CompactLineSeconds));
                        CurrentLyricRomaji = ReadingFor(activeLine);

                        // Find preceding non-empty line
                        int prevIdx = activeIdx - 1;
                        while (prevIdx >= 0 && string.IsNullOrWhiteSpace(_currentLyrics.Lines[prevIdx].Text))
                        {
                            prevIdx--;
                        }
                        PreviousLyricLine = prevIdx >= 0 ? _currentLyrics.Lines[prevIdx].Text : "";

                        // Find succeeding non-empty line
                        int nextIdx = activeIdx + 1;
                        while (nextIdx < _currentLyrics.Lines.Count && string.IsNullOrWhiteSpace(_currentLyrics.Lines[nextIdx].Text))
                        {
                            nextIdx++;
                        }
                        NextLyricLine = nextIdx < _currentLyrics.Lines.Count ? _currentLyrics.Lines[nextIdx].Text : "";

                        CurrentLyricPieces.Clear();
                        foreach (var p in PiecesFor(activeLine)) CurrentLyricPieces.Add(p);

                        PreviousLyricPieces.Clear();
                        if (prevIdx >= 0)
                        {
                            var prevLine = _currentLyrics.Lines[prevIdx];

                            foreach (var p in PiecesFor(prevLine)) PreviousLyricPieces.Add(p);
                        }

                        NextLyricPieces.Clear();
                        if (nextIdx < _currentLyrics.Lines.Count)
                        {
                            var nxtLine = _currentLyrics.Lines[nextIdx];

                            foreach (var p in PiecesFor(nxtLine)) NextLyricPieces.Add(p);
                        }

                        OnPropertyChanged(nameof(HasCurrentLyricRuby));
                        OnPropertyChanged(nameof(HasPreviousLyricRuby));
                        OnPropertyChanged(nameof(HasNextLyricRuby));
                    }
                }
            }
        });
    }

    public async Task<MochiDock.Core.MediaSeekResult> SeekMediaAsync(double seconds)
    {
        if(!double.IsFinite(seconds))return MochiDock.Core.MediaSeekResult.Unsupported;
        var result=await _mediaService.SeekToAsync(TimeSpan.FromSeconds(seconds));
        return result;
    }

    public async Task PollWeatherAsync()
    {
        if (IsWeatherLoading) return;
        App.Current.Dispatcher.Invoke(() =>
        {
            IsWeatherLoading = true;
            OnPropertyChanged(nameof(WeatherDisplayText));
            OnPropertyChanged(nameof(WeatherConditionText));
            OnPropertyChanged(nameof(WeatherCityDisplay));
        });

        string city = _settings?.WeatherCity ?? "Tokyo";
        var weather = await _weatherService.GetWeatherAsync(city);

        App.Current.Dispatcher.Invoke(() =>
        {
            IsWeatherLoading = false;
            WeatherInfo = weather;
            _currentWeatherCity = city;
            OnPropertyChanged(nameof(WeatherDisplayText));
            OnPropertyChanged(nameof(WeatherConditionText));
            OnPropertyChanged(nameof(WeatherIconText));
            OnPropertyChanged(nameof(WeatherCityDisplay));
            OnPropertyChanged(nameof(CompactCapsuleText));
            CheckRainSoon(weather);
        });
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
        if (task.IsCompleted) ShowHappyMoment();
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

    private long _prayerLocationGeneration;
    public async Task UpdatePrayerLocationAndScheduleAsync()
    {
        long generation=++_prayerLocationGeneration;
        _prayerCancellation?.Cancel(); ++_prayerGeneration; CurrentPrayerSchedule=null; PrayerAudio.Stop();
        string city=_settings.WeatherCity;
        try
        {
            var location=await _weatherService.ResolveLocationAsync(city);
            if(generation!=_prayerLocationGeneration||city!=_settings.WeatherCity)return;
            if(!location.Success)
            {
                if(!await _prayerTimesService.RestoreCachedLocationAsync(city))throw new InvalidOperationException("Cannot resolve prayer location. Connect once to resolve this city.");
                if(generation!=_prayerLocationGeneration||city!=_settings.WeatherCity)return;
            }
            else _prayerTimesService.SetCoordinates(location.Latitude,location.Longitude,city,location.ShortName);
            await UpdatePrayerScheduleAsync();
        }
        catch(Exception ex){if(generation==_prayerLocationGeneration){CurrentPrayerSchedule=null;PrayerError=ex.Message;OnPropertyChanged(nameof(PrayerError));}}
    }
    private long _prayerGeneration;
    private CancellationTokenSource? _prayerCancellation;
    private readonly MochiDock.Core.PrayerReminderGate _prayerReminders=new();
    private DateTimeOffset _lastPrayerAttempt;
    public string PrayerError {get;private set;}="";
    public async Task UpdatePrayerScheduleAsync()
    {
        if(!_settings.EnablePrayerTimes)return;
        long generation=++_prayerGeneration;
        _prayerCancellation?.Cancel();_prayerCancellation?.Dispose();_prayerCancellation=new();
        var token=_prayerCancellation.Token;
        _lastPrayerAttempt=DateTimeOffset.UtcNow;
        try
        {
            var schedule=await _prayerTimesService.FetchOrCalculateScheduleAsync(DateTime.UtcNow.Date,_settings.PrayerCalculationMethod,token);
            if(generation!=_prayerGeneration||token.IsCancellationRequested)return;
            CurrentPrayerSchedule=schedule;PrayerError="";
            _prayerReminders.Reset(DateTimeOffset.UtcNow);
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(generation==_prayerGeneration){CurrentPrayerSchedule=null;PrayerError=ex.Message;}}
        OnPropertyChanged(nameof(PrayerError));
    }
    public void UpdatePrayerSchedule()
    {
        if(!_settings.EnablePrayerTimes){PrayerAudio.Stop();return;}
        var now=DateTimeOffset.UtcNow;
        var schedule=CurrentPrayerSchedule;
        if(schedule==null){if((now-_lastPrayerAttempt).TotalSeconds>=30)_=UpdatePrayerScheduleAsync();return;}
        schedule.UpdateCountdown();
        NotifyPrayerPresentation();
        var date=TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId)).Date;
        if(date!=schedule.Date.Date){if((now-_lastPrayerAttempt).TotalSeconds>=30)_=UpdatePrayerScheduleAsync();return;}
        if(_isWorkstationLocked){_prayerReminders.Reset(now);DismissPrayerArrival();return;}
        if(_prayerArrivalUntil!=default && now>=_prayerArrivalUntil){DismissPrayerArrival();}
        foreach(var prayer in schedule.Instants())
        {
            string key=$"{schedule.Latitude}|{schedule.Longitude}|{prayer.Name}|{prayer.At:O}";
            if(_settings.EnablePrayerPreNotification&&_prayerReminders.Due(key+"-pre",prayer.At.AddMinutes(-10),now)&&ShouldShowNotification("Prayer"))
            {OnPropertyChanged(nameof(IsPrayerReminderVisible));}
            if(_prayerReminders.Due(key,prayer.At,now))
            {
                _ = PlayPrayerEventAsync();
                if (_settings.EnablePrayerTimes && _settings.ShowPrayerInCapsule && ShouldShowNotification("Prayer"))
                {
                    ShowPrayerArrival(prayer.Name, prayer.At, schedule.City);
                }
            }
        }
        _prayerReminders.Advance(now);
    }
    private async Task PlayPrayerEventAsync()
    {
        try{await PrayerAudio.PlayAsync(PrayerSoundMode);}
        catch(Exception ex){PrayerError=ex.Message;OnPropertyChanged(nameof(PrayerError));}
    }
    /// <summary>Plays the prayer chime once for the current sound mode (silent plays nothing); used when the user picks Tone.</summary>
    public void PreviewPrayerSound() => _ = PlayPrayerEventAsync();
    public void TogglePomodoro() => _pomodoroService.TogglePlayPause();
    public void ResetPomodoro() => _pomodoroService.Reset();
    public void StartPomodoro(int minutes) => _pomodoroService.StartWork(minutes);
    public void StartPomodoroBreak(int minutes) => _pomodoroService.StartBreak(minutes);

    public void ShowHardware(bool rememberSelection = true)
    {
        HardwareStats = _hardwareService.GetCurrentStats();
        if (rememberSelection) NavigateManually(IslandCardType.Hardware); else ActiveCardType = IslandCardType.Hardware;
        IsExpanded = true;
    }

    public async Task SendQuickReactionAsync(string emoji)
    {
        if (CurrentWhatsApp != null)
        {
            string jid = CurrentWhatsApp.FromJid, device = CurrentWhatsApp.DeviceId;
            bool sent = await _gowaService.SendReplyAsync(jid, emoji, device);
            if (sent) await MarkWhatsAppChatReadAsync(jid, device);
            DismissWhatsApp();
        }
    }

    // True when a notification tab was opened from the side companion bubble (one-time visit).
    private bool _isTransientCompanionView;
    private IslandCardType? _cardBeforeNotificationTakeover;

    /// <summary>Clears the one-time side-bubble flag (manual tab click, pin, etc.).</summary>
    public void ClearTransientCompanionView()
    {
        _isTransientCompanionView = false;
        _cardBeforeNotificationTakeover = null;
    }
    public bool IsTransientCompanionView => _isTransientCompanionView;

    private void SnapshotCardBeforeNotification()
    {
        if (ActiveCardType != IslandCardType.Email &&
            ActiveCardType != IslandCardType.WhatsApp &&
            ActiveCardType != IslandCardType.Discord &&
            ActiveCardType != IslandCardType.Apps &&
            ActiveCardType != IslandCardType.Summary)
        {
            _cardBeforeNotificationTakeover = ActiveCardType;
        }
    }

    private IslandCardType ResolveNotificationDismissFallback()
    {
        if (CurrentMedia.IsPlaying) return IslandCardType.Media;
        if (_cardBeforeNotificationTakeover.HasValue && ValidCardAvailable(_cardBeforeNotificationTakeover.Value.ToString()))
        {
            var card = _cardBeforeNotificationTakeover.Value;
            _cardBeforeNotificationTakeover = null;
            return card;
        }
        return Enum.Parse<IslandCardType>(ValidManualCard());
    }

    /// <summary>
    /// Called right before a hover/click expand from the collapsed main bubble.
    /// If the current tab was opened transiently from the side bubble and media is playing, return to Media.
    /// </summary>
    public void PrepareHoverExpand()
    {
        if (IsPrayerArrivalVisible)
        {
            DismissPrayerArrival();
            _isTransientCompanionView = false;
            ActiveCardType = IslandCardType.Prayer;
            return;
        }

        if (IsPrayerReminderVisible)
        {
            _isTransientCompanionView = false;
            ActiveCardType = IslandCardType.Prayer;
            return;
        }

        // If an incoming notification is active in the pill, ensure expanding opens directly to that notification card!
        if (IsNotificationInMainPill && CurrentRotationItem.HasValue)
        {
            _isTransientCompanionView = false;
            ActiveCardType = CurrentRotationItem.Value;
            return;
        }

        _isTransientCompanionView = false;
        ActiveCardType = Enum.Parse<IslandCardType>(ValidManualCard());
    }
    public void OpenCompanionActivity()
    {
        _isTransientCompanionView = _notificationQueue.Count > 0;
        var targetType = PrimaryCompanionItem ?? (_notificationQueue.Count > 0 ? _notificationQueue[0] : (IslandCardType?)null);

        if (targetType == IslandCardType.WhatsApp)
        {
            ShowWhatsApp(false);
        }
        else if (targetType == IslandCardType.Discord)
        {
            ShowDiscord(false);
        }
        else if (targetType == IslandCardType.Apps)
        {
            ShowApps(false);
        }
        else if (targetType == IslandCardType.Email)
        {
            ShowEmail(false);
        }
        else if (HasActiveMedia)
        {
            ShowMedia(false);
        }
    }

    public void OpenCurrentNotificationActivity()
    {
        var targetType = CurrentRotationItem ?? PrimaryCompanionItem;
        if (targetType == IslandCardType.WhatsApp) ShowWhatsApp(false);
        else if (targetType == IslandCardType.Discord) ShowDiscord(false);
        else if (targetType == IslandCardType.Apps) ShowApps(false);
        else if (targetType == IslandCardType.Email) ShowEmail(false);
        else if (HasActiveMedia) ShowMedia(false);
    }

    public async Task TogglePlayPauseAsync() => await _mediaService.TogglePlayPauseAsync();
    public async Task SkipNextAsync() => await _mediaService.SkipNextAsync();
    public async Task SkipPreviousAsync() => await _mediaService.SkipPreviousAsync();

    public void OpenEmail()
    {
        if (CurrentEmail != null)
        {
            var email = CurrentEmail;
            _mailService.OpenEmail(email);
            // Opening the email in Outlook/Mailspring handles it, so dismiss notification from the dock
            DismissEmail();
            IsExpanded = false;
        }
    }

    public void CopyEmailVerificationCode()
    {
        if (CurrentEmail != null && !string.IsNullOrWhiteSpace(CurrentEmail.ExtractedVerificationCode))
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(CurrentEmail.ExtractedVerificationCode);
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
        }
    }

    public void ReplyEmail()
    {
        if (CurrentEmail != null)
        {
            _mailService.OpenEmail(CurrentEmail);
            IsExpanded = false;
        }
    }

    public void MarkEmailAsRead()
    {
        if (CurrentEmail != null)
        {
            _mailService.MarkAsRead(CurrentEmail);
            DismissEmailMessage(CurrentEmail);
        }
    }

    public void SelectEmailMessage(EmailNotification? notif)
    {
        if (notif == null) return;
        CurrentEmail = notif;
    }

    public void DismissEmailMessage(EmailNotification? notif)
    {
        if (notif != null)
        {
            RecentEmailMessages.Remove(notif);
        }

        if (RecentEmailMessages.Count > 0)
        {
            CurrentEmail = RecentEmailMessages[0];
        }
        else
        {
            DismissEmail();
        }
    }

    public void ClearAllEmailMessages()
    {
        RecentEmailMessages.Clear();
        DismissEmail();
    }

    public void DismissEmail()
    {
        CurrentEmail = null;
        RecentEmailMessages.Clear();
        if (ActiveCardType == IslandCardType.Email)
        {
            if (HasActiveWhatsApp) ActiveCardType = IslandCardType.WhatsApp;
            else if (HasActiveDiscord) ActiveCardType = IslandCardType.Discord;
            else if (HasActiveApps) ActiveCardType = IslandCardType.Apps;
            else
            {
                ActiveCardType = ResolveNotificationDismissFallback();
                if (!_isPinned) IsExpanded = false;
            }

            MascotMood = _wasGaming ? MascotMood.Focused : (CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle);
        }
    }

    public void OpenDiscord()
    {
        _discordService.OpenDiscord();
        DismissDiscord();
        IsExpanded = false;
    }

    public void SelectDiscordMessage(DiscordNotification? notif)
    {
        if (notif == null) return;
        CurrentDiscord = notif;
    }

    public void DismissDiscordMessage(DiscordNotification? notif)
    {
        if (notif != null)
        {
            RecentDiscordMessages.Remove(notif);
        }

        if (RecentDiscordMessages.Count > 0)
        {
            CurrentDiscord = RecentDiscordMessages[0];
        }
        else
        {
            DismissDiscord();
        }
    }

    public void ClearAllDiscordMessages()
    {
        RecentDiscordMessages.Clear();
        DismissDiscord();
    }

    public void DismissDiscord()
    {
        CurrentDiscord = null;
        RecentDiscordMessages.Clear();
        if (ActiveCardType == IslandCardType.Discord)
        {
            if (HasActiveWhatsApp) ActiveCardType = IslandCardType.WhatsApp;
            else if (HasActiveEmail) ActiveCardType = IslandCardType.Email;
            else if (HasActiveApps) ActiveCardType = IslandCardType.Apps;
            else
            {
                ActiveCardType = ResolveNotificationDismissFallback();
                if (!_isPinned) IsExpanded = false;
            }

            MascotMood = _wasGaming ? MascotMood.Focused : (CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle);
        }
    }

    public async Task SendWhatsAppReplyAsync()
    {
        if (CurrentWhatsApp != null && !string.IsNullOrWhiteSpace(ReplyText))
        {
            string jid = CurrentWhatsApp.FromJid, device = CurrentWhatsApp.DeviceId;
            bool success = await _gowaService.SendReplyAsync(jid, ReplyText, device);

            if (success)
            {
                await MarkWhatsAppChatReadAsync(jid, device);
                ReplyText = "";
                DismissWhatsApp();
            }
        }
    }

    // After you answer a chat it counts as read: tell WhatsApp, so the phone and the other docks clear it too.
    private async Task MarkWhatsAppChatReadAsync(string jid, string device)
    {
        var ids = MochiDock.Core.WhatsAppReadIds.Pick(RecentWhatsAppMessages.Select(m => (m.RemoteId, m.FromJid, m.DeviceId)).ToList(), jid, device);
        if (ids.Count > 0) await _gowaService.MarkChatReadAsync(jid, ids, device);
    }

    public void SelectWhatsAppMessage(WhatsAppNotification? notif)
    {
        if (notif == null) return;
        CurrentWhatsApp = notif;
        ReplyText = "";
    }

    public void DismissWhatsAppMessage(WhatsAppNotification? notif)
    {
        if (notif != null)
        {
            RecentWhatsAppMessages.Remove(notif);

            // Also remove from conversation thread
            if (SelectedWhatsAppConversation != null)
            {
                SelectedWhatsAppConversation.Messages.Remove(notif);
                if (SelectedWhatsAppConversation.UnreadCount > 0)
                {
                    SelectedWhatsAppConversation.UnreadCount--;
                }
                if (SelectedWhatsAppConversation.Messages.Count == 0)
                {
                    ActiveWhatsAppConversations.Remove(SelectedWhatsAppConversation);
                    SelectedWhatsAppConversation = ActiveWhatsAppConversations.FirstOrDefault();
                }
            }
        }

        if (RecentWhatsAppMessages.Count > 0)
        {
            CurrentWhatsApp = RecentWhatsAppMessages[0];
        }
        else
        {
            DismissWhatsApp();
        }
    }

    public void ClearAllWhatsAppMessages()
    {
        RecentWhatsAppMessages.Clear();
        ActiveWhatsAppConversations.Clear();
        SelectedWhatsAppConversation = null;
        DismissWhatsApp();
    }

    public void DismissWhatsApp()
    {
        CurrentWhatsApp = null;
        RecentWhatsAppMessages.Clear();
        ActiveWhatsAppConversations.Clear();
        SelectedWhatsAppConversation = null;
        if (ActiveCardType == IslandCardType.WhatsApp)
        {
            if (HasActiveDiscord) ActiveCardType = IslandCardType.Discord;
            else if (HasActiveEmail) ActiveCardType = IslandCardType.Email;
            else if (HasActiveApps) ActiveCardType = IslandCardType.Apps;
            else
            {
                ActiveCardType = ResolveNotificationDismissFallback();
                if (!_isPinned) IsExpanded = false;
            }

            MascotMood = _wasGaming ? MascotMood.Focused : (CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle);
        }
    }

    /// <summary>True when "only notify for listed groups" is on and the shown notification is a group that is not on the list yet.</summary>
    public bool CanAllowCurrentWhatsAppGroup =>
        _settings.WhatsAppOnlyListedGroups && _currentWhatsApp is { IsGroup: true } current && !string.IsNullOrWhiteSpace(current.FromJid) &&
        !MochiDock.Core.GowaAccountPolicy.IsMuted(_settings.WhatsAppAllowedGroups, current.FromJid, current.DeviceId);

    /// <summary>Puts the group of the shown notification on the notify list, so it keeps notifying while only listed groups do.</summary>
    public void AllowCurrentWhatsAppGroup()
    {
        if (_currentWhatsApp is not { IsGroup: true } current || string.IsNullOrWhiteSpace(current.FromJid)) return;
        string key = _gowaService.MuteKeyFor(current.FromJid, current.DeviceId);
        if (!_settings.WhatsAppAllowedGroups.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            _settings.WhatsAppAllowedGroups.Add(key);
            _settings.WhatsAppChatNames[current.FromJid] = !string.IsNullOrWhiteSpace(current.GroupName) ? current.GroupName : current.SenderName;
            _gowaService.AddAllowedGroup(key);
            SettingsService.Save(_settings);
            SettingsChanged?.Invoke(SettingsApply.None);
        }
        OnPropertyChanged(nameof(CanAllowCurrentWhatsAppGroup));
    }

    public void MuteCurrentWhatsAppChat()
    {
        if (CurrentWhatsApp == null) return;
        string chatJid = CurrentWhatsApp.FromJid;
        string device = CurrentWhatsApp.DeviceId;
        if (string.IsNullOrWhiteSpace(chatJid)) return;

        // With several accounts the mute covers only the account that received this message.
        string muteKey = _gowaService.MuteKeyFor(chatJid, device);
        if (!_settings.MutedWhatsAppChatJids.Contains(muteKey, StringComparer.OrdinalIgnoreCase))
        {
            _settings.MutedWhatsAppChatJids.Add(muteKey);
            _settings.WhatsAppChatNames[chatJid] = CurrentWhatsApp.IsGroup && !string.IsNullOrWhiteSpace(CurrentWhatsApp.GroupName) ? CurrentWhatsApp.GroupName : CurrentWhatsApp.SenderName;
            _gowaService.AddMutedChat(muteKey);
            SettingsService.Save(_settings);
            SettingsChanged?.Invoke(SettingsApply.None);
        }

        // Dismiss this chat immediately
        if (SelectedWhatsAppConversation != null && string.Equals(SelectedWhatsAppConversation.ChatJid, chatJid, StringComparison.OrdinalIgnoreCase) &&
            MochiDock.Core.GowaAccountPolicy.AppliesTo(SelectedWhatsAppConversation.DeviceId, device))
        {
            ActiveWhatsAppConversations.Remove(SelectedWhatsAppConversation);
            SelectedWhatsAppConversation = ActiveWhatsAppConversations.FirstOrDefault();
        }

        for (int i = RecentWhatsAppMessages.Count - 1; i >= 0; i--)
        {
            if (string.Equals(RecentWhatsAppMessages[i].FromJid, chatJid, StringComparison.OrdinalIgnoreCase) &&
                MochiDock.Core.GowaAccountPolicy.AppliesTo(RecentWhatsAppMessages[i].DeviceId, device))
            {
                RecentWhatsAppMessages.RemoveAt(i);
            }
        }

        if (RecentWhatsAppMessages.Count > 0)
        {
            CurrentWhatsApp = RecentWhatsAppMessages[0];
        }
        else
        {
            DismissWhatsApp();
        }
    }

    public void OpenWhatsAppDashboard()
    {
        _gowaService.OpenDashboard();
    }

    public void LaunchWhatsAppApp()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("whatsapp:") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://web.whatsapp.com") { UseShellExecute = true });
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
        }
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
