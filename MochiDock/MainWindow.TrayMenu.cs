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

    // Right-click on the taskbar readout: opens the block's card, flips Left/Right side, jumps to settings, or hides the clicked block.
    private void ShowTaskbarWidgetMenu(MochiDock.Core.TaskbarBlockKind? kind)
    {
        try
        {
            var itemStyle = (Style)FindResource("ModernTrayMenuItemStyle");
            var sepStyle = (Style)FindResource("ModernTraySeparatorStyle");
            var menu = new ContextMenu { Style = (Style)FindResource("ModernTrayMenuStyle") };
            MenuItem Item(string header, string? icon, Action act)
            {
                var item = new MenuItem { Header = header, Style = itemStyle };
                if (icon != null) item.Icon = CreateTrayIcon(icon);
                item.Click += (s, e) => act();
                return item;
            }

            if (kind is { } k)
            {
                var (openLabel, openIcon, hideLabel) = k switch
                {
                    MochiDock.Core.TaskbarBlockKind.System => ("Open Performance", "NavGauge", "Hide CPU & GPU"),
                    MochiDock.Core.TaskbarBlockKind.MemoryDisk => ("Open Performance", "NavGauge", "Hide Memory & Disk"),
                    MochiDock.Core.TaskbarBlockKind.Network => ("Open Performance", "NavGauge", "Hide Network"),
                    MochiDock.Core.TaskbarBlockKind.Prayer => ("Open Prayer Times", "NavMoon", "Hide Prayer"),
                    MochiDock.Core.TaskbarBlockKind.Weather => ("Open Weather", "NavWeather", "Hide Weather"),
                    MochiDock.Core.TaskbarBlockKind.Batteries => ("Open Battery Page", "NavGauge", "Hide Device Batteries"),
                    _ => ("Open Performance", "NavGauge", "Hide Block"),
                };
                menu.Items.Add(Item(openLabel, openIcon, () => _viewModel.OpenTaskbarBlock(k)));
                if (k == MochiDock.Core.TaskbarBlockKind.Batteries)
                    menu.Items.Add(Item("Refresh Device Batteries", null, () => _viewModel.RefreshDeviceBatteries()));

                var targetZone = _viewModel.GetTaskbarBlockZone(k) == MochiDock.Core.TaskbarZone.Left
                    ? MochiDock.Core.TaskbarZone.Right
                    : MochiDock.Core.TaskbarZone.Left;
                menu.Items.Add(Item(targetZone == MochiDock.Core.TaskbarZone.Left ? "Move to Left side" : "Move to Right side", "NavFx",
                    () => _viewModel.SetTaskbarBlockZone(k, targetZone)));

                menu.Items.Add(new Separator { Style = sepStyle });
                menu.Items.Add(Item("Taskbar settings…", "NavGear", () => App.OpenSettings(MochiDock.Core.SettingsSections.Taskbar)));
                menu.Items.Add(Item("Settings…", "NavGear", () => App.OpenSettings()));
                menu.Items.Add(new Separator { Style = sepStyle });
                menu.Items.Add(Item(hideLabel, null, () => _viewModel.HideTaskbarBlock(k)));
            }
            else
            {
                menu.Items.Add(Item("Taskbar settings…", "NavGear", () => App.OpenSettings(MochiDock.Core.SettingsSections.Taskbar)));
                menu.Items.Add(Item("Settings…", "NavGear", () => App.OpenSettings()));
            }

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
            _ when assetName.StartsWith("Nav", StringComparison.Ordinal) => assetName,   // a theme icon key (NavGear, NavMoon, ...)
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

    // The tray menu, grouped by purpose (rebuilt on every right-click, so labels and checkmarks always show the current state):
    // the update (only while one is known), open, quick toggles, display, settings, exit.
    private ContextMenu CreateModernTrayMenu()
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("ModernTrayMenuStyle")
        };

        var itemStyle = (Style)FindResource("ModernTrayMenuItemStyle");
        var sepStyle = (Style)FindResource("ModernTraySeparatorStyle");
        void Sep() => menu.Items.Add(new Separator { Style = sepStyle });
        MenuItem Item(string header, string? icon, Action onClick)
        {
            var item = new MenuItem { Header = header, Style = itemStyle };
            if (icon != null) item.Icon = CreateTrayIcon(icon);
            item.Click += (s, e) => onClick();
            return item;
        }
        MenuItem Check(string header, bool isOn, Action onClick)
        {
            var item = new MenuItem { Header = header, Icon = CreateTrayCheck(isOn), Style = itemStyle };
            item.Click += (s, e) => onClick();
            return item;
        }

        // Update first, so a known release is never buried.
        if (_viewModel.HasUpdate)
        {
            var updateItem = new MenuItem { Header = _viewModel.TrayUpdateText, Icon = CreateTrayIcon("NavDownload"), Style = itemStyle };
            updateItem.Click += async (s, e) => await _viewModel.UpdateFromTrayAsync();
            menu.Items.Add(updateItem);
            Sep();
        }

        // Open
        menu.Items.Add(Item("Show Dock", "dango.png", () => { _viewModel.PrepareHoverExpand(); _viewModel.IsExpanded = true; }));
        menu.Items.Add(Item("Music", "NavMusic", () => _viewModel.ShowMedia()));
        menu.Items.Add(Item("Performance", "NavGauge", () => _viewModel.ShowHardware()));
        bool waEnabled = _viewModel.Settings.EnableWhatsApp;
        menu.Items.Add(Item(MochiDock.Core.OnboardingPolicy.WhatsAppTrayHeader(waEnabled), "NavMessage", () =>
        {
            if (MochiDock.Core.OnboardingPolicy.ShouldOpenWhatsAppSettings(waEnabled))
                App.OpenSettings(MochiDock.Core.SettingsSections.WhatsApp);
            else
                _viewModel.OpenWhatsAppDashboard();
        }));
        Sep();

        // Focus & Quick Controls
        menu.Items.Add(Check("Pin Dock Open", _viewModel.IsPinned, () => _viewModel.IsPinned = !_viewModel.IsPinned));
        menu.Items.Add(Check("Pin Stats to Pill", _viewModel.IsStatsPinnedToCapsule, () => _viewModel.IsStatsPinnedToCapsule = !_viewModel.IsStatsPinnedToCapsule));
        menu.Items.Add(CreateSnoozeMenu(itemStyle));
        menu.Items.Add(CreateQuickTogglesMenu(itemStyle, sepStyle));
        Sep();

        // Display
        var monitorSubmenu = new MenuItem
        {
            Header = "Monitor",
            Icon = CreateTrayIcon("NavMonitor"),
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

        var resSubmenu = new MenuItem
        {
            Header = "Display Resolution",
            Icon = CreateTrayIcon("NavFx"),
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
        resSubmenu.Items.Add(new Separator { Style = sepStyle });
        resSubmenu.Items.Add(toggleItem);
        menu.Items.Add(resSubmenu);

        // Back to the top centre of the dock monitor, brought to the front and opened.
        menu.Items.Add(Item("Reset Dock Position", "NavPin", () =>
        {
            _viewModel.UpdateSettings(x => x.IslandHorizontalRatio = null);
            PositionWindow();
            EnsureWindowTopmost();
            _viewModel.PrepareHoverExpand();
            _viewModel.IsExpanded = true;
        }));
        Sep();

        // Settings
        menu.Items.Add(Item("Settings…", "NavGear", () => App.OpenSettings()));
        menu.Items.Add(Item("Taskbar settings…", "NavGauge", () => App.OpenSettings(MochiDock.Core.SettingsSections.Taskbar)));
        AddMoreMenu(menu, itemStyle, sepStyle);
        Sep();

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

    // "Snooze Notifications", or "Notifications snoozed until 18:00" while a snooze runs (NotificationSnooze.TrayLabel).
    private MenuItem CreateSnoozeMenu(Style itemStyle)
    {
        MenuItem Action(string header, Action act)
        {
            var item = new MenuItem { Header = header, Style = itemStyle };
            item.Click += (s, e) => act();
            return item;
        }
        var snooze = new MenuItem
        {
            Header = MochiDock.Core.NotificationSnooze.TrayLabel(_viewModel.NotificationsSnoozedUntil, DateTime.Now),
            Icon = CreateTrayIcon("NavMoon"),
            Style = itemStyle
        };
        snooze.Items.Add(Action("For 1 Hour", () => _viewModel.SnoozeNotifications(TimeSpan.FromHours(1))));
        snooze.Items.Add(Action("For 8 Hours", () => _viewModel.SnoozeNotifications(TimeSpan.FromHours(8))));
        snooze.Items.Add(Action("Resume Notifications", () => _viewModel.SnoozeNotifications(TimeSpan.Zero)));
        return snooze;
    }

    private MenuItem CreateQuickTogglesMenu(Style itemStyle, Style sepStyle)
    {
        MenuItem Toggle(string header, bool isOn, Action flip)
        {
            var item = new MenuItem { Header = header, Icon = CreateTrayCheck(isOn), Style = itemStyle };
            item.Click += (s, e) => flip();
            return item;
        }

        var quick = new MenuItem { Header = "Quick Toggles", Icon = CreateTrayIcon("NavBell"), Style = itemStyle };
        quick.Items.Add(Toggle("Notification Sounds", _viewModel.EnableSounds, () => _viewModel.EnableSounds = !_viewModel.EnableSounds));
        quick.Items.Add(Toggle("Quiet while Gaming", _viewModel.Settings.EnableGamingDnd, () => _viewModel.UpdateSettings(x => x.EnableGamingDnd = !x.EnableGamingDnd, MochiDock.ViewModels.SettingsApply.CapsuleRefresh)));
        quick.Items.Add(Toggle("Hide from Screen Capture", _viewModel.ExcludeFromScreenCapture, () => _viewModel.ExcludeFromScreenCapture = !_viewModel.ExcludeFromScreenCapture));
        quick.Items.Add(Toggle("Hide in Fullscreen Apps", _viewModel.HideInFullscreen, () => _viewModel.HideInFullscreen = !_viewModel.HideInFullscreen));
        quick.Items.Add(new Separator { Style = sepStyle });

        // Start with Windows registers or removes the logon task, which takes a moment: the item is disabled meanwhile.
        var startupItem = new MenuItem { Header = "Start with Windows", Icon = CreateTrayCheck(_settings.StartWithWindows), Style = itemStyle };
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
        quick.Items.Add(startupItem);
        return quick;
    }

    // Tray: secondary preferences, lyrics controls, and settings/data shortcuts. Rebuilt on every right-click.
    private void AddMoreMenu(ContextMenu menu, Style itemStyle, Style sepStyle)
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

        var more = new MenuItem { Header = "More", Icon = CreateTrayIcon("NavTray"), Style = itemStyle };

        var lyrics = new MenuItem { Header = "Lyrics", Icon = CreateTrayIcon("NavMusic"), Style = itemStyle };
        lyrics.Items.Add(Toggle("Synced Lyrics", _viewModel.EnableSyncedLyrics, () => _viewModel.EnableSyncedLyrics = !_viewModel.EnableSyncedLyrics));
        lyrics.Items.Add(Toggle("Lyrics Pronunciation", _viewModel.ShowLyricsPronunciation, () => _viewModel.ShowLyricsPronunciation = !_viewModel.ShowLyricsPronunciation));
        lyrics.Items.Add(Toggle("Furigana Readings", _viewModel.ShowLyricsFurigana, () => _viewModel.ShowLyricsFurigana = !_viewModel.ShowLyricsFurigana));
        var timing = new MenuItem { Header = $"Lyric Timing: {MochiDock.Core.LyricOffset.Label(_viewModel.LyricsOffsetMs)}", Style = itemStyle };
        timing.Items.Add(Action("Lines Earlier (+0.1s)", () => _viewModel.NudgeLyricsOffset(MochiDock.Core.LyricOffset.StepMs)));
        timing.Items.Add(Action("Lines Later (−0.1s)", () => _viewModel.NudgeLyricsOffset(-MochiDock.Core.LyricOffset.StepMs)));
        timing.Items.Add(Action("Reset", () => _viewModel.ResetLyricsOffset()));
        lyrics.Items.Add(timing);
        more.Items.Add(lyrics);

        more.Items.Add(Toggle("Hardware Alerts", _viewModel.EnableHardwareAlerts, () => _viewModel.EnableHardwareAlerts = !_viewModel.EnableHardwareAlerts));
        more.Items.Add(Toggle("Charger & Low-Battery Pop-ups", _viewModel.EnablePowerAlerts, () => _viewModel.EnablePowerAlerts = !_viewModel.EnablePowerAlerts));
        more.Items.Add(new Separator { Style = sepStyle });

        more.Items.Add(Action("Reload Settings", async () =>
        {
            try { await _viewModel.ReloadSettingsAsync(); PositionWindow(); }
            catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "MochiDock settings"); }
        }));
        more.Items.Add(Action("Show Settings File", () => RevealInExplorer(SettingsService.SettingsFilePath)));
        more.Items.Add(Action("Open Data Folder", () => RevealInExplorer(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MochiDock"))));
        menu.Items.Add(more);
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
