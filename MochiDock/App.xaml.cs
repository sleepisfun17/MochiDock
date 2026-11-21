using System.IO;
using System.Threading;
using System.Windows;
using MochiDock.Services;
using MochiDock.ViewModels;

namespace MochiDock;

public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;
    private MediaService? _mediaService;
    private IMailService? _mailService;
    private GowaService? _gowaService;
    private DiscordNotificationService? _discordService;
    private WeatherService? _weatherService;
    private PomodoroService? _pomodoroService;
    private PrayerTimesService? _prayerTimesService;
    private LrclibService? _lrclibService;
    private TasksService? _tasksService;
    private FileShelfService? _fileShelfService;
    private ClipboardService? _clipboardService;
    private MainWindow? _mainWindow;
    private SettingsWindowService? _settingsWindows;

    /// <summary>Opens the Advanced Settings window (or activates it), optionally at a section id or deep-link alias.</summary>
    public static void OpenSettings(string? section = null) => (Current as App)?._settingsWindows?.Show(section);

    /// <summary>Applies the capsule theme to the Advanced Settings window's caption.</summary>
    public static void ApplySettingsWindowTheme(string theme) => (Current as App)?._settingsWindows?.ApplyTheme(theme);

    /// <summary>Closes the Advanced Settings window, committing pending edits, before settings are flushed.</summary>
    public static void CloseSettingsWindow() => (Current as App)?._settingsWindows?.CloseForExit();
    private FxRatesService? _fxRates;
    private DeviceBatteryService? _deviceBatteries;
    private PowerLogService? _powerLog;
    private StatsHistoryService? _statsHistory;
    private CalendarFeedService? _calendarFeed;

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend) { try { _powerLog?.Checkpoint(); _statsHistory?.FlushAsync(); } catch (Exception ex) { MochiDock.Services.AppLog.Warn("App", ex); } }
    }

    // Windows shutdown, restart or sign-out: close and write the power session now, because the process may be ended soon after.
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        try { Task.WhenAll(_powerLog?.StopAsync() ?? Task.CompletedTask, _statsHistory?.FlushAsync() ?? Task.CompletedTask).Wait(TimeSpan.FromSeconds(2)); }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("App", ex); }
        base.OnSessionEnding(e);
    }
    private System.Net.Http.HttpClient? _fxHttp;

    // Writes the crash text where the user can always find it: the log folder (never read-only) and, best effort, next to the exe.
    private static void RecordCrash(string source, Exception? exception, string? text, bool terminating)
    {
        try
        {
            if (exception != null) MochiDock.Services.AppLog.Error(source, exception);
            string body = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + source + (terminating ? " (terminating)" : "") + Environment.NewLine + text + Environment.NewLine;
            string logDir = MochiDock.Services.AppLog.Directory;
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "crash.log"), body);
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), body); }
            catch (System.Exception ex) { MochiDock.Services.AppLog.Warn("App", ex); }
        }
        catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("Crash logging failed: " + ex.Message); }
    }

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            RecordCrash("AppDomain", e.ExceptionObject as Exception, e.ExceptionObject?.ToString(), e.IsTerminating);

        DispatcherUnhandledException += (s, e) =>
        {
            RecordCrash("Dispatcher", e.Exception, e.Exception.ToString(), true);
            e.Handled = false;
        };

        // A faulted background task nobody awaited (for example from the WhatsApp service) must leave a trace, not vanish.
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            MochiDock.Services.AppLog.Error("Task", e.Exception?.Flatten() ?? new Exception("Unobserved task exception"));
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // WPF runs OnStartup as soon as an App object exists and its dispatcher runs, even without Run(). When another program
        // (a test script, a designer) creates one, it must stay inert: no mutex, no services, and above all no reading or
        // saving the user's real settings.
        if (!string.Equals(System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name, "MochiDock", StringComparison.OrdinalIgnoreCase))
        {
            base.OnStartup(e);
            return;
        }

        const string mutexName = "Global\\MochiDockSingleInstanceMutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            // Another instance is already running! Exit immediately.
            System.Windows.MessageBox.Show(
                "MochiDock is already running in the background.\nPlease check your System Tray (taskbar overflow chevron ^) to access it.",
                "MochiDock",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        SettingsService.PersistenceError += ReportPersistenceError;
        AtomicJsonStore.Error += message => System.Diagnostics.Debug.WriteLine(message);

        try
        {
            var settings = SettingsService.Load();
            _mediaService = new MediaService();
            _mailService = new CompositeMailService(
                new ImapMailService(new MailKitImapSessionFactory(), MailCredentialStore.Default) { FilterAds = settings.FilterAds },
                new MailspringMailService(),
                new OutlookMailService { FilterAds = settings.FilterAds, OnlyListedFolders = settings.OutlookOnlyListedFolders, TrackedFolders = MochiDock.Core.OutlookFolderPolicy.Split(MochiDock.Core.OutlookFolderPolicy.Join(settings.OutlookTrackedFolders)) }
            );
            (_mailService as CompositeMailService)?.Find<ImapMailService>()?.Configure(settings.ImapAccounts);
            _gowaService = new GowaService();
            _gowaService.ConfigureFromSettings(settings);
            _discordService = new DiscordNotificationService();
            _weatherService = new WeatherService();
            _pomodoroService = new PomodoroService();
            _prayerTimesService = new PrayerTimesService();
            _lrclibService = new LrclibService();

            _tasksService = new TasksService();
            _tasksService.PersistenceError += ReportPersistenceError;
            _fileShelfService = new FileShelfService();
            _clipboardService = new ClipboardService();

            var viewModel = new MainViewModel(_mediaService, _mailService, _gowaService, _discordService, _weatherService, _pomodoroService, _prayerTimesService, _lrclibService, _tasksService, _fileShelfService, _clipboardService);
            _fxHttp=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromSeconds(8)};
            _fxRates=new FxRatesService(new KeylessFxProvider(_fxHttp), initialPairs: settings.FxPairs);viewModel.ConfigureFx(_fxRates);_ = _fxRates.StartAsync(settings.ShowFxRatesTab);
            _deviceBatteries = new DeviceBatteryService();viewModel.ConfigureDeviceBatteries(_deviceBatteries);
            _powerLog = new PowerLogService();_powerLog.PersistenceError += message => System.Diagnostics.Debug.WriteLine(message);viewModel.ConfigurePowerLog(_powerLog);
            _statsHistory = new StatsHistoryService();_statsHistory.PersistenceError += message => MochiDock.Services.AppLog.Warn("StatsHistory", message);viewModel.ConfigureStatsHistory(_statsHistory);
            // A suspend leaves a gap the log ignores; save what we have first in case the PC never comes back (battery runs out).
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _calendarFeed = new CalendarFeedService(new DpapiProtector());viewModel.ConfigureCalendar(_calendarFeed);
            _mainWindow = new MainWindow(viewModel);
            _settingsWindows = new SettingsWindowService(_mainWindow, viewModel);
            _mainWindow.Show();
            viewModel.StartUpdateChecks();

            _ = Task.Run(async () =>
            {
                try { await _mediaService.InitializeAsync(); } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("App", logEx); }
                try { _mailService.Start(); } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("App", logEx); }
                try { _gowaService.Start(); } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("App", logEx); }
                try { await _discordService.StartAsync(); } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("App", logEx); }
            });
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_error.log"), ex.ToString());
                System.Windows.MessageBox.Show(
                    $"MochiDock encountered an error during startup:\n\n{ex.Message}\n\nDetails have been written to startup_error.log",
                    "MochiDock Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("App", logEx); }
        }
    }

    private void ReportPersistenceError(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(() => System.Windows.MessageBox.Show(message, "MochiDock could not save data", MessageBoxButton.OK, MessageBoxImage.Warning));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { (_mainWindow?.DataContext as MochiDock.ViewModels.MainViewModel)?.StopUpdateChecks(); }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("App", ex); }
        try { _settingsWindows?.CloseForExit(); }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("App", ex); }
        try
        {
            Task.WhenAll(_fxRates?.StopAsync() ?? Task.CompletedTask, _deviceBatteries?.StopAsync() ?? Task.CompletedTask, _powerLog?.StopAsync() ?? Task.CompletedTask, _statsHistory?.FlushAsync() ?? Task.CompletedTask, SettingsService.FlushAsync(), _tasksService?.FlushAsync() ?? Task.CompletedTask, _fileShelfService?.StopServerAsync() ?? Task.CompletedTask).Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Persistence flush: " + ex.Message); }
        _fxRates?.Dispose();_fxHttp?.Dispose();_deviceBatteries?.Dispose();
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;_powerLog?.Dispose();
        _calendarFeed?.Dispose();
        PrayerAudio.Stop();
        _mailService?.Stop();
        _gowaService?.Stop();
        _discordService?.Stop();

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("App", logEx); }

        _mediaService?.Dispose();
        base.OnExit(e);
    }
}
