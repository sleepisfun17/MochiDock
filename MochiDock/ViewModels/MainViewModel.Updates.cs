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

    private void NotifyUpdateState()
    {
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
        try { _updateCts?.Cancel(); } catch (Exception ex) { AppLog.Warn("Update", ex); }
    }

    public async Task CheckForUpdatesAsync(bool manual)
    {
        if (!manual && !_settings.CheckForUpdates) return;
        if (_updateBusy) return;
        _updateBusy = true;
        var cts = _updateCts ??= new CancellationTokenSource();
        try
        {
            _updateStatus = "Checking...";
            OnPropertyChanged(nameof(UpdateStatusText));
            var result = await _updateCheck.CheckAsync(UpdateCheckService.CurrentVersion, cts.Token);
            _availableUpdate = result.Offer;
            _updateStatus = result.State == UpdateCheckState.Available ? "" : result.Message;
            if (result.State != UpdateCheckState.Failed) UpdateSettings(x => x.LastUpdateCheckUtc = DateTime.UtcNow);
        }
        catch (OperationCanceledException) { _updateStatus = ""; }
        finally
        {
            _updateBusy = false;
            NotifyUpdateState();
        }
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
        _downloadCts = new CancellationTokenSource();
        _updateStatus = "Starting the download…";
        NotifyUpdateState();
        bool launched = false;
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                _updateStatus = $"Downloading {p.Done / 1048576.0:0.0} of {p.Total / 1048576.0:0.0} MB…";
                OnPropertyChanged(nameof(UpdateStatusText));
            });
            string setup = await _updateInstaller.DownloadAsync(asset, progress, _downloadCts.Token);
            _downloadCts = null;
            _updateStatus = "Installing, MochiDock will restart in a moment…";
            NotifyUpdateState();
            UpdateInstaller.Launch(setup);
            launched = true;
            await Task.Delay(400);   // let the setup start before this copy leaves
            System.Windows.Application.Current?.Shutdown();
        }
        catch (OperationCanceledException) { _updateStatus = "Update cancelled."; }
        catch (UpdateVerificationException ex) { _updateStatus = ex.Message; AppLog.Warn("Update", ex); }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TaskCanceledException)
        {
            _updateStatus = "The update could not be downloaded or started. Try again, or install it from the release page.";
            AppLog.Warn("Update", ex);
        }
        finally
        {
            if (!launched)
            {
                _installing = false;
                _downloadCts = null;
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
