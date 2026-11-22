using System.Diagnostics;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.ViewModels;

/// <summary>
/// Update check against GitHub Releases, and the one-click update: only when the user asks (and confirms) does MochiDock download the
/// release's setup, check it against the release's SHA-256, and run it silently; it then restarts on the new version.
/// </summary>
public partial class MainViewModel
{
    private readonly UpdateCheckService _updateCheck = new();
    private CancellationTokenSource? _updateCts;
    private System.Windows.Threading.DispatcherTimer? _updateTimer;
    private UpdateOffer? _availableUpdate;
    private string _updateStatus = "";
    private bool _updateBusy;
    private readonly UpdateInstaller _updateInstaller = new();
    private CancellationTokenSource? _downloadCts;
    private bool _installing;
    private bool? _installedCopy;
    // The "update available" capsule notice: shown once per version after an automatic check, retried while something else holds it back.
    private bool _noticeIsUpdate;
    private string? _pendingUpdateNotice;
    private System.Windows.Threading.DispatcherTimer? _updateNoticeRetry;
    private int? _downloadPercent;

    /// <summary>True when this copy runs from the setup's install folder (only that copy updates itself).</summary>
    public bool IsInstalledCopy => _installedCopy ??= UpdateInstaller.IsInstalledCopy();
    /// <summary>An update can be installed from inside the app: the release has a verifiable setup and this is the installed copy.</summary>
    public bool CanInstallUpdate => _availableUpdate?.Installer != null && IsInstalledCopy && !_installing;
    public bool IsUpdateDownloading => _downloadCts != null;
    /// <summary>Why an available update has to be installed by hand, or empty.</summary>
    public string UpdateInstallHint => _availableUpdate == null || _installing ? "" :
        _availableUpdate.Installer == null ? "This release has no setup MochiDock can check, so install it from the release page." :
        !IsInstalledCopy ? "This copy wasn't installed with the setup; download the update from the release page." : "";
    public bool HasUpdateInstallHint => UpdateInstallHint.Length > 0;

    public string CurrentVersionText => UpdateCheckService.CurrentVersion;
    public UpdateOffer? AvailableUpdate => _availableUpdate;
    public bool HasUpdate => _availableUpdate != null;
    public string TrayUpdateText => _availableUpdate == null ? "" : CanInstallUpdate ? $"Update to {_availableUpdate.Version}…" : $"Update available: {_availableUpdate.Version}";

    public string UpdateStatusText
    {
        get
        {
            if (_installing && _updateStatus.Length > 0) return _updateStatus;
            if (_availableUpdate != null) return $"Version {_availableUpdate.Version} is available (you have {CurrentVersionText}).";
            string last = _settings.LastUpdateCheckUtc is { } t ? $" Last checked {t.ToLocalTime():g}." : "";
            return string.IsNullOrEmpty(_updateStatus) ? $"Version {CurrentVersionText}.{last}" : $"Version {CurrentVersionText}. {_updateStatus}{last}";
        }
    }

    /// <summary>The update button in the quick settings header: the version, "Checking…", "Update to x", or the download progress.</summary>
    public UpdateChipState UpdateChipState =>
        UpdateChipPolicy.State(_updateBusy, _availableUpdate != null, CanInstallUpdate, IsUpdateDownloading, _installing);
    public string UpdateChipText => UpdateChipPolicy.Text(UpdateChipState, CurrentVersionText, _availableUpdate?.Version, _downloadPercent);
    public bool IsUpdateChipHighlighted => UpdateChipPolicy.IsHighlighted(UpdateChipState);
    public string UpdateChipToolTip => UpdateChipState switch
    {
        UpdateChipState.Current => UpdateStatusText + " Click to check now.",
        UpdateChipState.Install => UpdateStatusText + " Click to update.",
        UpdateChipState.Available => UpdateStatusText + " " + UpdateInstallHint + " Click to open the release page.",
        UpdateChipState.Downloading => UpdateStatusText + " Click to cancel.",
        _ => UpdateStatusText,
    };

    private void NotifyUpdateChip()
    {
        OnPropertyChanged(nameof(UpdateChipState));
        OnPropertyChanged(nameof(UpdateChipText));
        OnPropertyChanged(nameof(IsUpdateChipHighlighted));
        OnPropertyChanged(nameof(UpdateChipToolTip));
    }

    /// <summary>A click on the header's update button: check, install (after its confirmation), open the release page, or cancel.</summary>
    public Task UpdateChipClickAsync()
    {
        switch (UpdateChipState)
        {
            case UpdateChipState.Current: return CheckForUpdatesAsync(manual: true);
            case UpdateChipState.Install: return InstallUpdateAsync();
            case UpdateChipState.Available: OpenUpdatePage(); break;
            case UpdateChipState.Downloading: CancelUpdateDownload(); break;
        }
        return Task.CompletedTask;
    }

    private void NotifyUpdateState()
    {
        NotifyUpdateChip();
        OnPropertyChanged(nameof(AvailableUpdate));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(TrayUpdateText));
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(CanInstallUpdate));
        OnPropertyChanged(nameof(IsUpdateDownloading));
        OnPropertyChanged(nameof(UpdateInstallHint));
        OnPropertyChanged(nameof(HasUpdateInstallHint));
    }

    /// <summary>First check 30 seconds after start (never delays startup), then every 12 hours while "Check for updates" is on.</summary>
    public void StartUpdateChecks()
    {
        if (_updateTimer != null) return;
        UpdateInstaller.CleanUp();   // a setup left from an earlier update is no longer needed
        _updateCts = new CancellationTokenSource();
        _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _updateTimer.Tick += (s, e) =>
        {
            _updateTimer!.Interval = TimeSpan.FromHours(12);
            _ = CheckForUpdatesAsync(manual: false);
        };
        _updateTimer.Start();
    }

    public void StopUpdateChecks()
    {
        _updateTimer?.Stop();
        _updateTimer = null;
        _updateNoticeRetry?.Stop();
        _pendingUpdateNotice = null;
        try { _updateCts?.Cancel(); } catch (Exception ex) { AppLog.Warn("Update", ex); }
        try { _updateCts?.Dispose(); } catch { }
        _updateCts = null;
        try { _downloadCts?.Cancel(); } catch (Exception ex) { AppLog.Warn("Update", ex); }
    }

    public async Task CheckForUpdatesAsync(bool manual)
    {
        if (!manual && !_settings.CheckForUpdates) return;
        if (_updateBusy || _installing) return;
        _updateBusy = true;
        var cts = _updateCts ??= new CancellationTokenSource();
        try
        {
            _updateStatus = "Checking...";
            OnPropertyChanged(nameof(UpdateStatusText));
            NotifyUpdateChip();
            var result = await _updateCheck.CheckAsync(UpdateCheckService.CurrentVersion, cts.Token);
            if (result.State != UpdateCheckState.Failed)
            {
                _availableUpdate = result.Offer;
                _updateStatus = result.State == UpdateCheckState.Available ? "" : result.Message;
                UpdateSettings(x => x.LastUpdateCheckUtc = DateTime.UtcNow);
            }
            else if (_availableUpdate == null)
            {
                _updateStatus = result.Message;
            }
            if (UpdateAnnouncePolicy.ShouldAnnounce(result.Offer?.Version, _settings.LastAnnouncedUpdate, manual))
            {
                _pendingUpdateNotice = result.Offer!.Version;
                TryShowUpdateNotice();
            }
        }
        catch (OperationCanceledException) { _updateStatus = ""; }
        finally
        {
            _updateBusy = false;
            NotifyUpdateState();
        }
    }

    public bool CheckForUpdates
    {
        get => _settings?.CheckForUpdates ?? true;
        set
        {
            if (_settings == null || _settings.CheckForUpdates == value) return;
            UpdateSettings(s => s.CheckForUpdates = value, SettingsApply.None);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Shows the pending "update available" notice when nothing else holds the capsule (gaming, lock, screen off, a message,
    /// prayer or reminder, snooze or quiet hours); otherwise tries again every five minutes. The version counts as announced only
    /// once the notice was shown. Tapping it opens the quick settings, whose header button then reads "Update to x".
    /// </summary>
    private void TryShowUpdateNotice()
    {
        if (_pendingUpdateNotice is not { } version || _availableUpdate?.Version != version) { _pendingUpdateNotice = null; _updateNoticeRetry?.Stop(); return; }
        bool blocked = _hardwarePresentationSuppressed || _wasGaming || _isWorkstationLocked || !_displayAwake || IsNotificationInMainPill ||
                       IsPrayerArrivalVisible || IsPrayerReminderVisible || IsHardwareNoticeVisible || !ShouldShowNotification("Updates");
        if (blocked)
        {
            if (_updateNoticeRetry == null)
            {
                _updateNoticeRetry = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
                _updateNoticeRetry.Tick += (_, _) => TryShowUpdateNotice();
            }
            if (!_updateNoticeRetry.IsEnabled) _updateNoticeRetry.Start();
            return;
        }
        _updateNoticeRetry?.Stop();
        _pendingUpdateNotice = null;
        UpdateSettings(x => x.LastAnnouncedUpdate = version, SettingsApply.None);
        AppLog.Warn("Update", $"Update notice: {version}.");
        _noticeIsAi = false; _noticeIsAgenda = false; _noticeIsGame = false; _noticeIsDevice = false; _noticeIsWeather = false; _noticeHeatModule = null;
        _noticeIsUpdate = true;
        _hardwareNoticeText = CanInstallUpdate ? $"MochiDock {version} is available: tap to update" : $"MochiDock {version} is available: tap for details";
        _hardwareNoticeUntil = DateTimeOffset.UtcNow.AddSeconds(8);
        _hardwareNoticeTimer.Stop(); _hardwareNoticeTimer.Start();
        NotifyHardwareNotice();
    }

    /// <summary>
    /// The tray's update item: installs when that is possible, otherwise opens the release page.
    /// </summary>
    public Task UpdateFromTrayAsync() => CanInstallUpdate ? InstallUpdateAsync() : Task.Run(OpenUpdatePage);

    /// <summary>
    /// After one confirmation: downloads the release's setup, checks its size and SHA-256 against the release, starts it silently and
    /// closes MochiDock, which the setup starts again on the new version. Any failure leaves the current version running.
    /// </summary>
    public async Task InstallUpdateAsync()
    {
        var offer = _availableUpdate;
        if (offer?.Installer is not { } asset || !IsInstalledCopy || _installing) return;
        var answer = System.Windows.MessageBox.Show($"Install MochiDock {offer.Version} now?\n\nMochiDock closes for about 20 seconds while the update installs, then starts again. Your settings are kept.",
            "Update MochiDock", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        _installing = true;
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        _downloadPercent = null;
        _updateStatus = "Starting the download…";
        NotifyUpdateState();
        bool launched = false;
        bool downloadDone = false;
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                if (downloadDone || !_installing) return;
                _updateStatus = $"Downloading {p.Done / 1048576.0:0.0} of {p.Total / 1048576.0:0.0} MB…";
                _downloadPercent = p.Total > 0 ? (int)(p.Done * 100 / p.Total) : null;
                OnPropertyChanged(nameof(UpdateStatusText));
                NotifyUpdateChip();
            });
            string setup = await _updateInstaller.DownloadAsync(asset, progress, _downloadCts.Token);
            downloadDone = true;
            _downloadCts.Dispose();
            _downloadCts = null;
            _downloadPercent = null;
            _updateStatus = "Installing, MochiDock will restart in a moment…";
            NotifyUpdateState();
            if (!UpdateInstaller.Launch(setup))
            {
                _updateStatus = "The update installer could not be started.";
                return;
            }
            launched = true;
            await Task.Delay(400);   // let the setup start before this copy leaves
            System.Windows.Application.Current?.Shutdown();
        }
        catch (OperationCanceledException) { _updateStatus = "Update cancelled."; }
        catch (UpdateVerificationException ex) { _updateStatus = ex.Message; AppLog.Warn("Update", ex); }
        catch (Exception ex)
        {
            _updateStatus = "The update could not be downloaded or started. Try again, or install it from the release page.";
            AppLog.Warn("Update", ex);
        }
        finally
        {
            downloadDone = true;
            _downloadCts?.Dispose();
            _downloadCts = null;
            if (!launched)
            {
                _installing = false;
                NotifyUpdateState();
            }
        }
    }

    public void CancelUpdateDownload() => _downloadCts?.Cancel();

    /// <summary>Opens the release page in the browser. Only addresses of this project's releases are ever opened.</summary>
    public void OpenUpdatePage()
    {
        string? url = _availableUpdate?.Url;
        if (url == null || !url.StartsWith(UpdateFeed.ReleasePrefix, StringComparison.Ordinal)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Warn("Update", ex); }
    }
}
