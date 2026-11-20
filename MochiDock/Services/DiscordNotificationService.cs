using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using MochiDock.Models;

namespace MochiDock.Services;

public class DiscordNotificationService
{
    private UserNotificationListener? _listener;
    private readonly HashSet<uint> _seenIds = new();
    private CancellationTokenSource? _cts;

    public event Action<DiscordNotification>? MessageReceived;

    public async Task StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            if (_listener == null) return;

            var status = await _listener.RequestAccessAsync();
            if (status != UserNotificationListenerAccessStatus.Allowed)
            {
                Debug.WriteLine("UserNotificationListener access for Discord not allowed.");
                return;
            }

            // Seed existing notifications so we don't alert on past toasts
            var existing = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in existing)
            {
                _seenIds.Add(n.Id);
            }

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => PollLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start Discord notification listener: {ex.Message}");
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
                    foreach (var n in notifs)
                    {
                        if (_seenIds.Contains(n.Id)) continue;
                        _seenIds.Add(n.Id);

                        string appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                        string aumid = n.AppInfo?.AppUserModelId ?? "";

                        if (appName.Contains("Discord", StringComparison.OrdinalIgnoreCase) ||
                            aumid.Contains("Discord", StringComparison.OrdinalIgnoreCase))
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
            catch { }

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
