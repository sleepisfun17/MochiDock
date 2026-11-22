using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using MochiDock.Services;

namespace MochiDock;

public partial class MainWindow
{
    private void ShowModernTrayMenu()
    {
        try
        {
            if (_windowClosed) return;
            _trayContextMenu = CreateModernTrayMenu();
            _trayContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            _trayContextMenu.PlacementTarget = this;

            // Bring main window handle to foreground so clicking outside smoothly dismisses popup
            var helper = new WindowInteropHelper(this);
            SetForegroundWindow(helper.Handle);

            _trayContextMenu.IsOpen = true;
            _ = RefreshStartupStateAsync();
        }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("Window", ex); }
    }

    private async Task RefreshStartupStateAsync()
    {
        try
        {
            bool startWithWindows = await StartupService.RefreshAsync();
            if (!_windowClosed && _settings.StartWithWindows != startWithWindows)
            {
                _settings.StartWithWindows = startWithWindows;
                SettingsService.Save(_settings);
            }
        }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("Window", ex); }
    }

    // Right-click on the taskbar battery widget: refresh, open the Battery page, hide the widget. Raised on the UI thread by the widget.
    private void ShowBatteryWidgetMenu()
    {
        try
        {
            var itemStyle = (Style)FindResource("ModernTrayMenuItemStyle");
            var menu = new ContextMenu { Style = (Style)FindResource("ModernTrayMenuStyle") };
            MenuItem Item(string header, Action act)
            {
                var item = new MenuItem { Header = header, Style = itemStyle };
                item.Click += (s, e) => act();
                return item;
            }
            menu.Items.Add(Item("Refresh Now", () => _viewModel.RefreshDeviceBatteries()));
            menu.Items.Add(Item("Open Battery Page", () => _viewModel.OpenBatteryModule()));
            menu.Items.Add(new Separator { Style = (Style)FindResource("ModernTraySeparatorStyle") });
            menu.Items.Add(Item("Hide Taskbar Widget", () => _viewModel.ShowDeviceBatteriesOnTaskbar = false));
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.PlacementTarget = this;
            SetForegroundWindow(new WindowInteropHelper(this).Handle);
            menu.IsOpen = true;
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Window", logEx); }
    }

    private static FrameworkElement CreateTrayIcon(string assetName, double size = 16)
    {
        string? key = assetName switch
        {
            "music.png" => "NavMusic", "desktop.png" => "NavMonitor",
            "zap.png" => "NavGauge", "whatsapp.png" => "NavMessage", "pin.png" => "NavPin",
            _ => null
        };
        if (key is not null && Application.Current.TryFindResource(key) is FrameworkElement vector)
        {
            vector.Width = vector.Height = size;
            return vector;
        }
        return new System.Windows.Controls.Image
        {
            Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/Icons/{assetName}", UriKind.Absolute)),
            Width = size,
            Height = size,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
    }

    private static TextBlock CreateTrayCheck(bool isChecked)
    {
        var tb = new TextBlock
        {
            Text = isChecked ? "✓" : "",
            FontWeight = FontWeights.Bold,
            FontSize = 12
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "IslandAccentBrush");
        return tb;
    }

    private ContextMenu CreateModernTrayMenu()
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("ModernTrayMenuStyle")
        };

        var itemStyle = (Style)FindResource("ModernTrayMenuItemStyle");
        var sepStyle = (Style)FindResource("ModernTraySeparatorStyle");

        // 1. Show Dock
        var showItem = new MenuItem
        {
            Header = "Show Dock",
            Icon = CreateTrayIcon("dango.png"),
            Style = itemStyle
        };
        showItem.Click += (s, e) => { _viewModel.PrepareHoverExpand(); _viewModel.IsExpanded = true; };
        menu.Items.Add(showItem);

        // 2. Music Controls
        var musicItem = new MenuItem
        {
            Header = "Music Controls",
            Icon = CreateTrayIcon("music.png"),
            Style = itemStyle
        };
        musicItem.Click += (s, e) => _viewModel.ShowMedia();
        menu.Items.Add(musicItem);

        // 3. Select Monitor Submenu
        var monitorSubmenu = new MenuItem
        {
            Header = "Select Monitor",
            Icon = CreateTrayIcon("desktop.png"),
            Style = itemStyle
        };

        var monitors = MultiMonitorHelper.GetAllMonitors();
        foreach (var m in monitors)
        {
            bool isCurrent = m.DeviceName.Equals(_settings.TargetMonitor, StringComparison.OrdinalIgnoreCase) ||
                             (string.IsNullOrEmpty(_settings.TargetMonitor) && m.IsPrimary);

            var monItem = new MenuItem
            {
                Header = m.DisplayLabel,
                Icon = CreateTrayCheck(isCurrent),
                Style = itemStyle
            };
            monItem.Click += (s, e) => SwitchMonitor(m);
            monitorSubmenu.Items.Add(monItem);
        }
        menu.Items.Add(monitorSubmenu);

        // 3b. Display Resolution Submenu
        var resSubmenu = new MenuItem
        {
            Header = "Display Resolution",
            Icon = CreateTrayIcon("desktop.png"),
            Style = itemStyle
        };

        // Native and gaming come from the dock monitor's own modes, so another PC shows its own sizes, never this one's.
        var targets = GetResolutionTargets();
        void AddResolutionItem((int Width, int Height) size, string label)
        {
            var item = new MenuItem
            {
                Header = label,
                Icon = CreateTrayCheck(targets.Current is { } cur && cur.Width == size.Width && cur.Height == size.Height),
                Style = itemStyle
            };
            item.Click += (s, e) =>
            {
                var screen = MultiMonitorHelper.GetTargetScreen(_settings.TargetMonitor);
                if (_resolutionService.ApplyResolution(size.Width, size.Height, screen?.DeviceName))
                {
                    PositionWindow();
                    ShowResolutionNotice(label);
                }
            };
            resSubmenu.Items.Add(item);
        }
        if (targets.Native is { } native)
            AddResolutionItem(native, MochiDock.Core.DisplayResolutionPolicy.FormatLabel(native.Width, native.Height, isNative: true));
        if (targets.Gaming is { } gaming && gaming != targets.Native)
            AddResolutionItem(gaming, MochiDock.Core.DisplayResolutionPolicy.FormatLabel(gaming.Width, gaming.Height) + " Gaming");

        var toggleItem = new MenuItem
        {
            Header = $"Toggle Resolution ({(MochiDock.Core.HotkeyParser.TryParse(_viewModel.Settings.ResolutionHotkey, out var trayHotkey, out _) ? trayHotkey.Text : MochiDock.Core.HotkeyParser.Default)})",
            Style = itemStyle
        };
        toggleItem.Click += (s, e) => ToggleResolutionFromHotkey();
        resSubmenu.Items.Add(new Separator { Style = (Style)FindResource("ModernTraySeparatorStyle") });
        resSubmenu.Items.Add(toggleItem);

        menu.Items.Add(resSubmenu);

        // 4. System Stats
        var statsItem = new MenuItem
        {
            Header = "System Performance",
            Icon = CreateTrayIcon("zap.png"),
            Style = itemStyle
        };
        statsItem.Click += (s, e) => _viewModel.ShowHardware();
        menu.Items.Add(statsItem);

        // 4a. Manual refresh of wireless device batteries (only while the feature is on)
        if (_viewModel.EnableDeviceBatteries)
        {
            var refreshBatteries = new MenuItem
            {
                Header = "Refresh Device Batteries",
                Icon = CreateTrayIcon("zap.png"),
                Style = itemStyle
            };
            refreshBatteries.Click += (s, e) => _viewModel.RefreshDeviceBatteries();
            menu.Items.Add(refreshBatteries);
        }

        // 4a. Update notice (only while a newer release is known)
        if (_viewModel.HasUpdate)
        {
            var updateItem = new MenuItem { Header = _viewModel.TrayUpdateText, Icon = CreateTrayIcon("zap.png"), Style = itemStyle };
            updateItem.Click += async (s, e) => await _viewModel.UpdateFromTrayAsync();
            menu.Items.Add(updateItem);
        }

        // 4b. Advanced Settings window
        var advancedItem = new MenuItem
        {
            Header = "Settings…",
            Icon = CreateTrayIcon("desktop.png"),
            Style = itemStyle
        };
        advancedItem.Click += (s, e) => App.OpenSettings();
        menu.Items.Add(advancedItem);

        // 5. WhatsApp Pairing / Setup
        bool waEnabled = _viewModel.Settings.EnableWhatsApp;
        var waItem = new MenuItem
        {
            Header = MochiDock.Core.OnboardingPolicy.WhatsAppTrayHeader(waEnabled),
            Icon = CreateTrayIcon("whatsapp.png"),
            Style = itemStyle
        };
        waItem.Click += (s, e) =>
        {
            if (MochiDock.Core.OnboardingPolicy.ShouldOpenWhatsAppSettings(waEnabled))
                App.OpenSettings(MochiDock.Core.SettingsSections.WhatsApp);
            else
                _viewModel.OpenWhatsAppDashboard();
        };
        menu.Items.Add(waItem);

        // 6. Toggle Pin
        var pinItem = new MenuItem
        {
            Header = _viewModel.IsPinned ? "Unpin Dock" : "Pin Dock Open",
            Icon = CreateTrayIcon("pin.png"),
            Style = itemStyle
        };
        pinItem.Click += (s, e) => _viewModel.IsPinned = !_viewModel.IsPinned;
        menu.Items.Add(pinItem);

        // 7. Pin Stats to Capsule
        var pinStatsItem = new MenuItem
        {
            Header = _viewModel.IsStatsPinnedToCapsule ? "Unpin Stats from Pill" : "Pin Stats to Pill",
            Icon = CreateTrayIcon("zap.png"),
            Style = itemStyle
        };
        pinStatsItem.Click += (s, e) => _viewModel.IsStatsPinnedToCapsule = !_viewModel.IsStatsPinnedToCapsule;
        menu.Items.Add(pinStatsItem);

        // 8. Start with Windows
        var startupItem = new MenuItem
        {
            Header = "Start with Windows",
            Icon = CreateTrayCheck(_settings.StartWithWindows),
            Style = itemStyle
        };
        startupItem.Click += async (s, e) =>
        {
            startupItem.IsEnabled = false;
            try
            {
                _settings.StartWithWindows = await StartupService.ToggleAsync();
                SettingsService.Save(_settings);
            }
            catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "MochiDock startup"); }
            finally { startupItem.IsEnabled = true; }
        };
        menu.Items.Add(startupItem);

        // 9. Reset Dock Position & Bring to Front
        var resetPosItem = new MenuItem
        {
            Header = "Reset Dock Position (Top Center)",
            Icon = CreateTrayIcon("desktop.png"),
            Style = itemStyle
        };
        resetPosItem.Click += (s, e) =>
        {
            _viewModel.UpdateSettings(x => x.IslandHorizontalRatio = null);
            PositionWindow();
            EnsureWindowTopmost();
            _viewModel.PrepareHoverExpand();
            _viewModel.IsExpanded = true;
        };
        menu.Items.Add(resetPosItem);

        menu.Items.Add(new Separator { Style = sepStyle });
        AddPreferencesMenus(menu, itemStyle);
        menu.Items.Add(new Separator { Style = sepStyle });

        // 10. Exit
        var exitItem = new MenuItem
        {
            Header = "Exit",
            Icon = new TextBlock { Text = "✕", FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("IslandRedBrush"), FontWeight = FontWeights.Bold },
            Style = itemStyle
        };
        exitItem.Click += (s, e) => Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    // Tray: preferences and settings/data shortcuts. The menu is rebuilt on every right-click, so checkmarks always reflect the current state.
    private void AddPreferencesMenus(ContextMenu menu, Style itemStyle)
    {
        MenuItem Toggle(string header, bool isOn, Action flip)
        {
            var item = new MenuItem { Header = header, Icon = CreateTrayCheck(isOn), Style = itemStyle };
            item.Click += (s, e) => flip();
            return item;
        }
        MenuItem Action(string header, Action act)
        {
            var item = new MenuItem { Header = header, Style = itemStyle };
            item.Click += (s, e) => act();
            return item;
        }

        var prefs = new MenuItem { Header = "Preferences", Icon = CreateTrayIcon("zap.png"), Style = itemStyle };
        prefs.Items.Add(Toggle("Synced Lyrics", _viewModel.EnableSyncedLyrics, () => _viewModel.EnableSyncedLyrics = !_viewModel.EnableSyncedLyrics));
        prefs.Items.Add(Toggle("Lyrics Pronunciation", _viewModel.ShowLyricsPronunciation, () => _viewModel.ShowLyricsPronunciation = !_viewModel.ShowLyricsPronunciation));
        prefs.Items.Add(Toggle("Furigana Readings", _viewModel.ShowLyricsFurigana, () => _viewModel.ShowLyricsFurigana = !_viewModel.ShowLyricsFurigana));

        var timing = new MenuItem { Header = $"Lyric Timing: {MochiDock.Core.LyricOffset.Label(_viewModel.LyricsOffsetMs)}", Style = itemStyle };
        timing.Items.Add(Action("Lines Earlier (+0.1s)", () => _viewModel.NudgeLyricsOffset(MochiDock.Core.LyricOffset.StepMs)));
        timing.Items.Add(Action("Lines Later (−0.1s)", () => _viewModel.NudgeLyricsOffset(-MochiDock.Core.LyricOffset.StepMs)));
        timing.Items.Add(Action("Reset", () => _viewModel.ResetLyricsOffset()));
        prefs.Items.Add(timing);

        prefs.Items.Add(new Separator { Style = (Style)FindResource("ModernTraySeparatorStyle") });
        prefs.Items.Add(Toggle("Notification Sounds", _viewModel.EnableSounds, () => _viewModel.EnableSounds = !_viewModel.EnableSounds));
        prefs.Items.Add(Toggle("Hide in Fullscreen Apps", _viewModel.HideInFullscreen, () => _viewModel.HideInFullscreen = !_viewModel.HideInFullscreen));
        prefs.Items.Add(Toggle("Hardware Alerts", _viewModel.EnableHardwareAlerts, () => _viewModel.EnableHardwareAlerts = !_viewModel.EnableHardwareAlerts));
        prefs.Items.Add(Toggle("Charger & Low-Battery Pop-ups", _viewModel.EnablePowerAlerts, () => _viewModel.EnablePowerAlerts = !_viewModel.EnablePowerAlerts));
        menu.Items.Add(prefs);

        var snooze = new MenuItem { Header = "Snooze Notifications", Style = itemStyle };
        snooze.Items.Add(Action("For 1 Hour", () => _viewModel.SnoozeNotifications(TimeSpan.FromHours(1))));
        snooze.Items.Add(Action("For 8 Hours", () => _viewModel.SnoozeNotifications(TimeSpan.FromHours(8))));
        snooze.Items.Add(Action("Resume Notifications", () => _viewModel.SnoozeNotifications(TimeSpan.Zero)));
        menu.Items.Add(snooze);

        var files = new MenuItem { Header = "Settings & Data", Icon = CreateTrayIcon("desktop.png"), Style = itemStyle };
        files.Items.Add(Action("Reload Settings", async () =>
        {
            try { await _viewModel.ReloadSettingsAsync(); PositionWindow(); }
            catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "MochiDock settings"); }
        }));
        files.Items.Add(Action("Show Settings File", () => RevealInExplorer(SettingsService.SettingsFilePath)));
        files.Items.Add(Action("Open Data Folder", () => RevealInExplorer(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MochiDock"))));
        menu.Items.Add(files);
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
            {
                var folder = System.IO.Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Open in Explorer: " + ex.Message); }
    }
}
