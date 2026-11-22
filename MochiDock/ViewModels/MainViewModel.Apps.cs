using System.Collections.ObjectModel;
using MochiDock.Core;
using MochiDock.Models;
using MochiDock.Services;

namespace MochiDock.ViewModels;

/// <summary>
/// The Apps card: Windows notifications from the apps the user watches (Settings > Notifications > Other apps), read by the
/// same listener as Discord. Follows the Discord card's flow: history of 10, mute, snooze and quiet hours, gaming queue, lock summary.
/// </summary>
public partial class MainViewModel
{
    private AppNotification? _currentApp;
    private int _queuedGamingAppsCount;
    private int _lockedAppsCount;

    public ObservableCollection<AppNotification> RecentAppMessages { get; } = new();
    public bool HasActiveApps => _currentApp != null;
    public bool IsAppsActive => _activeCardType == IslandCardType.Apps;

    public AppNotification? CurrentApp
    {
        get => _currentApp;
        set
        {
            _currentApp = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OpenAppButtonText));
            OnPropertyChanged(nameof(HeaderBadgeText));
            if (value != null) { if (!_recordSilently) EnqueueNotification(IslandCardType.Apps); }
            else RemoveFromNotificationQueue(IslandCardType.Apps);
        }
    }

    public string OpenAppButtonText => string.IsNullOrWhiteSpace(_currentApp?.AppName) ? "Open app" : $"Open {_currentApp.AppName}";

    /// <summary>The watched apps, as set in Settings > Notifications > Other apps.</summary>
    public IReadOnlyList<WatchedApp> WatchedApps => _settings.WatchedApps ??= new();

    /// <summary>Apps that sent a notification since MochiDock started and are not watched yet, for the settings picker.</summary>
    public IReadOnlyList<WatchedApp> UnwatchedRecentApps =>
        _discordService.RecentApps.Where(r => !WatchedApps.Any(w => AppNotificationFilter.Same(w, r))).ToList();

    /// <summary>Null while starting; false when Windows denied notification access (Discord and other apps then show nothing).</summary>
    public bool? NotificationAccessAllowed => _discordService.AccessAllowed;

    public void WatchApp(WatchedApp app)
    {
        var list = AppNotificationFilter.Add(WatchedApps, app);
        if (list.Count == WatchedApps.Count) return;
        UpdateSettings(s => s.WatchedApps = list, SettingsApply.None);
        SyncWatchedApps();
    }

    public void UnwatchApp(WatchedApp app)
    {
        var list = WatchedApps.Where(w => !AppNotificationFilter.Same(w, app)).ToList();
        UpdateSettings(s => s.WatchedApps = list, SettingsApply.None);
        SyncWatchedApps();
    }

    private void SyncWatchedApps()
    {
        _discordService.SetWatchedApps(WatchedApps);
        OnPropertyChanged(nameof(WatchedApps));
        OnPropertyChanged(nameof(UnwatchedRecentApps));
    }

    private void OnAppMessageReceived(AppNotification notif)
    {
        App.BeginOnUI(() =>
        {
            RecentAppMessages.Insert(0, notif);
            while (RecentAppMessages.Count > 10) RecentAppMessages.RemoveAt(RecentAppMessages.Count - 1);

            bool shown = ShouldShowNotification("Apps");
            SetCurrentSilently(!shown, () => CurrentApp = notif);
            if (!shown) return;
            bool isGaming = _hardwareService.IsGameRunning();
            string preview = $"🔔 {notif.AppName}: {notif.Title}";

            if (_isWorkstationLocked && _settings.EnableOvernightGreeting)
            {
                _lockedAppsCount++;
                _lockedPreviews.Add(preview);
                return;
            }
            if (isGaming && GamingDndActive)
            {
                _queuedGamingAppsCount++;
                _queuedGamingPreviews.Add(preview);
                return;
            }

            SnapshotCardBeforeNotification();
            SoundService.PlayNotificationSound();
            ActiveCardType = IslandCardType.Apps;
            MascotMood = MascotMood.Excited;
            if (_settings.AutoExpandNotificationsOnArrival)
            {
                IsExpanded = true;
                _autoCollapseTimer.Stop();
                _autoCollapseTimer.Start();
            }
            else StartNotificationTakeover();
        });
    }

    public void ShowApps(bool rememberSelection = true)
    {
        if (rememberSelection) NavigateManually(IslandCardType.Apps); else ActiveCardType = IslandCardType.Apps;
        IsExpanded = true;
    }

    public void OpenCurrentApp()
    {
        if (_currentApp != null) DiscordNotificationService.OpenApp(_currentApp.Aumid);
        DismissApps();
    }

    public void SelectAppMessage(AppNotification? notif)
    {
        if (notif != null) CurrentApp = notif;
    }

    public void DismissAppMessage(AppNotification? notif)
    {
        if (notif != null) RecentAppMessages.Remove(notif);
        if (RecentAppMessages.Count > 0) CurrentApp = RecentAppMessages[0];
        else DismissApps();
    }

    public void DismissApps()
    {
        CurrentApp = null;
        RecentAppMessages.Clear();
        if (ActiveCardType == IslandCardType.Apps)
        {
            if (HasActiveWhatsApp) ActiveCardType = IslandCardType.WhatsApp;
            else if (HasActiveEmail) ActiveCardType = IslandCardType.Email;
            else if (HasActiveDiscord) ActiveCardType = IslandCardType.Discord;
            else
            {
                ActiveCardType = ResolveNotificationDismissFallback();
                if (!_isPinned) IsExpanded = false;
            }
        }
    }

    private static string AppsCount(int count) => $"{count} app notification{(count > 1 ? "s" : "")}";
}
