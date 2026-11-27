using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MochiDock.Core;
using MochiDock.Models;

namespace MochiDock.Services;

/// <summary>
/// Reads Windows toast notifications (UserNotificationListener). Discord's go to the Discord card; those of the apps the user
/// watches (Settings > Notifications > Other apps) go to the Apps card. Every app seen is remembered for the settings picker.
/// </summary>
public class DiscordNotificationService
{
    private UserNotificationListener? _listener;
    private readonly HashSet<uint> _seenIds = new();
    private CancellationTokenSource? _cts;
    private readonly object _gate = new();
    private IReadOnlyList<WatchedApp> _watched = Array.Empty<WatchedApp>();
    private bool _watchAll;
    private List<WatchedApp> _recentApps = new();

    public event Action<DiscordNotification>? MessageReceived;
    public event Action<AppNotification>? AppMessageReceived;
    /// <summary>Raised (on the polling thread) when an app not seen before sends a notification.</summary>
    public event Action? RecentAppsChanged;
    /// <summary>Raised when <see cref="AccessAllowed"/> is determined or changes.</summary>
    public event Action? AccessChanged;

    /// <summary>Null until the listener starts; false when Windows denied notification access.</summary>
    public bool? AccessAllowed { get; private set; }

    /// <summary>Apps whose notifications were seen since MochiDock started, newest first (Discord excluded).</summary>
    public IReadOnlyList<WatchedApp> RecentApps { get { lock (_gate) return _recentApps.ToList(); } }

    public void SetWatchedApps(IEnumerable<WatchedApp>? apps, bool watchAll = false)
    {
        lock (_gate)
        {
            _watched = (apps ?? Array.Empty<WatchedApp>()).ToList();
            _watchAll = watchAll;
        }
    }

    public async Task StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            if (_listener == null) return;

            var status = await _listener.RequestAccessAsync();
            AccessAllowed = status == UserNotificationListenerAccessStatus.Allowed;
            AccessChanged?.Invoke();
            if (AccessAllowed != true)
            {
                AppLog.Warn("Notifications", $"Windows notification access was not allowed ({status}); Discord and other apps cannot be shown.");
                return;
            }

            // Seed existing notifications so we don't alert on past toasts, while remembering non-Discord apps for the picker.
            var existing = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            bool seededRecent = false;
            foreach (var n in existing)
            {
                _seenIds.Add(n.Id);
                string appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                string aumid = n.AppInfo?.AppUserModelId ?? "";
                if ((appName.Length > 0 || aumid.Length > 0) &&
                    !AppNotificationFilter.IsDiscord(appName, aumid) &&
                    !AppNotificationFilter.IsSelf(appName, aumid))
                {
                    lock (_gate)
                    {
                        _recentApps = AppNotificationFilter.Remember(_recentApps, new WatchedApp(appName, aumid));
                        seededRecent = true;
                    }
                }
            }
            if (seededRecent) RecentAppsChanged?.Invoke();

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => PollLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            AccessAllowed = false;
            AccessChanged?.Invoke();
            AppLog.Warn("Notifications", ex);
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_listener != null)
                {
                    var notifs = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
                    // Forget ids no longer in the action centre, so the set does not grow for the whole session.
                    _seenIds.IntersectWith(notifs.Select(x => x.Id));
                    foreach (var n in notifs)
                    {
                        if (_seenIds.Contains(n.Id)) continue;
                        _seenIds.Add(n.Id);

                        string appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                        string aumid = n.AppInfo?.AppUserModelId ?? "";

                        if (!AppNotificationFilter.IsDiscord(appName, aumid))
                        {
                            await HandleOtherAppAsync(n, appName, aumid).ConfigureAwait(false);
                            continue;
                        }
                        {
                            var toastBinding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                            if (toastBinding != null)
                            {
                                var texts = toastBinding.GetTextElements();
                                string rawSender = texts.Count > 0 ? texts[0].Text : "Discord";
                                string message = texts.Count > 1 ? texts[1].Text : "";

                                string channelOrServer = "";
                                bool isDm = true;

                                // DMs typically have sender name directly, servers often format like "User (Server #channel)" or "Server Name"
                                if (rawSender.Contains("(#") || rawSender.Contains(" in "))
                                {
                                    isDm = false;
                                    channelOrServer = rawSender;
                                }

                                MessageReceived?.Invoke(new DiscordNotification
                                {
                                    Sender = rawSender,
                                    Message = message,
                                    ChannelOrServer = channelOrServer,
                                    IsDirectMessage = isDm,
                                    Timestamp = DateTime.Now
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { AppLog.Warn("Notifications", ex); }

            try
            {
                await Task.Delay(1200, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task HandleOtherAppAsync(UserNotification n, string appName, string aumid)
    {
        if ((appName.Length == 0 && aumid.Length == 0) || AppNotificationFilter.IsSelf(appName, aumid)) return;
        WatchedApp? match;
        bool newApp;
        lock (_gate)
        {
            var seen = new WatchedApp(appName, aumid);
            newApp = !_recentApps.Any(r => AppNotificationFilter.Same(r, seen));
            _recentApps = AppNotificationFilter.Remember(_recentApps, seen);
            match = AppNotificationFilter.Match(appName, aumid, _watched, _watchAll);
        }
        if (newApp) RecentAppsChanged?.Invoke();
        if (match == null) return;

        var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
        string name = appName.Length > 0 ? appName : match.Name;
        var (title, message) = AppNotificationFilter.Split(binding?.GetTextElements().Select(t => t.Text), name);
        AppMessageReceived?.Invoke(new AppNotification
        {
            AppName = name,
            Aumid = aumid,
            Title = title,
            Message = message,
            Icon = await LoadLogoAsync(n).ConfigureAwait(false),
            Timestamp = DateTime.Now
        });
    }

    private static async Task<ImageSource?> LoadLogoAsync(UserNotification n)
    {
        try
        {
            var reference = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64));
            if (reference == null) return null;
            using var stream = await reference.OpenReadAsync();
            using var managed = stream.AsStreamForRead();
            var memory = new MemoryStream();
            await managed.CopyToAsync(memory).ConfigureAwait(false);
            memory.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 64;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) { AppLog.Warn("Notifications", ex); return null; }
    }

    /// <summary>Brings up a watched app through its Start-menu entry (AUMID).</summary>
    public static void OpenApp(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + aumid) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Warn("Notifications", ex); }
    }

    public void OpenDiscord()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "discord://",
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                // Fallback to activating existing Discord window
                var procs = Process.GetProcessesByName("Discord");
                if (procs.Length > 0)
                {
                    IntPtr hwnd = procs[0].MainWindowHandle;
                    if (hwnd != IntPtr.Zero)
                    {
                        SetForegroundWindow(hwnd);
                    }
                }
            }
            catch { }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
