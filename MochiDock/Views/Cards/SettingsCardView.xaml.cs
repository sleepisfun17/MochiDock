using System;
using System.Windows;
using System.Windows.Threading;
using MochiDock.Models;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Cards;

/// <summary>
/// The quick Settings tab of the dock: Focus &amp; Alerts, Privacy &amp; Dock, Look &amp; Mascot, and collapsible Tabs with section deep-links.
/// Everything else lives in the Advanced Settings window.
/// The view model owns the only <see cref="AppSettings"/> object; this card reads it for display and writes through
/// <see cref="MainViewModel.UpdateSettings"/>.
/// </summary>
public partial class SettingsCardView : UserControl
{
    private DispatcherTimer? _snoozeRefreshTimer;
    private bool _isLoadingPreferences;
    private MainViewModel? _subscribed;

    public SettingsCardView()
    {
        InitializeComponent();

        Loaded += (s, e) => { Subscribe(); ReloadAllSettings(); };

        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible) ReloadAllSettings();
            else _snoozeRefreshTimer?.Stop();
        };

        Unloaded += (s, e) => { _snoozeRefreshTimer?.Stop(); Unsubscribe(); };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    // Changes made in the Advanced Settings window or the tray show up here without reopening the tab.
    private void Subscribe()
    {
        if (ReferenceEquals(_subscribed, ViewModel)) return;
        Unsubscribe();
        _subscribed = ViewModel;
        if (_subscribed != null) _subscribed.SettingsChanged += OnSettingsChanged;
    }

    private void Unsubscribe()
    {
        if (_subscribed != null) _subscribed.SettingsChanged -= OnSettingsChanged;
        _subscribed = null;
    }

    private void OnSettingsChanged(SettingsApply effects) => Dispatcher.BeginInvoke(LoadPreferences);

    private void ReloadAllSettings()
    {
        if (ViewModel == null) return;
        LoadPreferences();
        StartSnoozeMonitor();
    }

    private void StartSnoozeMonitor()
    {
        _snoozeRefreshTimer?.Stop();
        _snoozeRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _snoozeRefreshTimer.Tick += (s, e) => UpdateSnoozeUi();
        _snoozeRefreshTimer.Start();
        UpdateSnoozeUi();
    }

    private void UpdateSnoozeUi()
    {
        long snoozeTicks = ViewModel?.Settings.SnoozeUntilTicks ?? 0;
        if (snoozeTicks > DateTime.Now.Ticks)
        {
            var until = new DateTime(snoozeTicks);
            var remaining = until - DateTime.Now;
            SnoozeTimeText.Text = $"{until:HH:mm} ({Math.Ceiling(remaining.TotalMinutes)}m remaining)";
            SnoozeActiveBanner.Visibility = Visibility.Visible;
            ClearSnoozeBtn.Visibility = Visibility.Visible;
        }
        else
        {
            SnoozeActiveBanner.Visibility = Visibility.Collapsed;
            ClearSnoozeBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void LoadPreferences()
    {
        if (ViewModel == null) return;
        var settings = ViewModel.Settings;
        _isLoadingPreferences = true;
        try
        {
            MuteEmailCheck.IsChecked = settings.MuteEmail;
            MuteDiscordCheck.IsChecked = settings.MuteDiscord;
            MuteWhatsAppCheck.IsChecked = settings.MuteWhatsApp;
            GamingDndCheck.IsChecked = settings.EnableGamingDnd;
            RemindersCheck.IsChecked = settings.EnableAgendaReminders;

            string theme = MochiDock.Core.ThemePolicy.Normalize(settings.CapsuleTheme);
            ThemeMatchaRadio.IsChecked = theme == MochiDock.Core.ThemePolicy.Matcha;
            ThemeHojichaRadio.IsChecked = theme == MochiDock.Core.ThemePolicy.Hojicha;
            ThemeKuroNightRadio.IsChecked = theme == MochiDock.Core.ThemePolicy.KuroNight;
            ThemeClassicRadio.IsChecked = theme == MochiDock.Core.ThemePolicy.Default;
            string mascot = MochiDock.Core.MascotChoice.Normalize(settings.Mascot);
            MascotAutoRadio.IsChecked = mascot == MochiDock.Core.MascotChoice.Auto;
            MascotMochiRadio.IsChecked = mascot == MochiDock.Core.MascotChoice.Mochi;
            MascotMatchaMochiRadio.IsChecked = mascot == MochiDock.Core.MascotChoice.MatchaMochi;
            MascotHojichaMochiRadio.IsChecked = mascot == MochiDock.Core.MascotChoice.HojichaMochi;
            MascotKuroRadio.IsChecked = mascot == MochiDock.Core.MascotChoice.Kuro;
            MascotShiroRadio.IsChecked = mascot == MochiDock.Core.MascotChoice.Shiro;

            var badgeStyle = MochiDock.Core.MascotBadge.ParseStyle(settings.MascotBadgeStyle);
            QuickBadgeCharmRadio.IsChecked = badgeStyle == MochiDock.Core.MascotBadgeStyle.Charm;
            QuickBadgeBubbleRadio.IsChecked = badgeStyle == MochiDock.Core.MascotBadgeStyle.Bubble;
            QuickBadgeStickerRadio.IsChecked = badgeStyle == MochiDock.Core.MascotBadgeStyle.Sticker;
            QuickBadgeOffRadio.IsChecked = badgeStyle == MochiDock.Core.MascotBadgeStyle.Off;

            bool notch = string.Equals(settings.DockStyle, "Notch", StringComparison.OrdinalIgnoreCase);
            DockNotchRadio.IsChecked = notch;
            DockFloatingRadio.IsChecked = !notch;
            UpdateQuickScaleText();
            UpdateSnoozeUi();
            UpdateTabsSummary(settings);
        }
        finally
        {
            _isLoadingPreferences = false;
        }
    }

    private void UpdateTabsSummary(AppSettings settings)
    {
        int enabled = 0;
        if (settings.ShowStatsTab) enabled++;
        if (settings.ShowWeatherTab) enabled++;
        if (settings.ShowPomodoroTab) enabled++;
        if (settings.EnablePrayerTimes) enabled++;
        if (settings.ShowFileShelfTab) enabled++;
        if (settings.ShowClipboardTab) enabled++;
        if (settings.ShowTodayTab) enabled++;
        if (settings.ShowAiUsageTab) enabled++;
        if (settings.ShowScriptTab) enabled++;
        if (settings.ShowDownloadsTab) enabled++;
        if (settings.ShowFxRatesTab) enabled++;
        TabsSummaryText.Text = $"{enabled} of 11 shown";
    }

    private void BadgeStyle_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isLoadingPreferences) return;
        if (sender is System.Windows.Controls.RadioButton { Tag: string style })
            ViewModel?.ApplyMascotBadgeStyle(style);
    }

    // The Mascot row mirrors the Mascot dropdown in Advanced Settings: both save the same setting and both reload when it changes.
    private void MascotRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isLoadingPreferences) return;
        string chosen = MascotMochiRadio.IsChecked == true ? MochiDock.Core.MascotChoice.Mochi
                      : MascotMatchaMochiRadio.IsChecked == true ? MochiDock.Core.MascotChoice.MatchaMochi
                      : MascotHojichaMochiRadio.IsChecked == true ? MochiDock.Core.MascotChoice.HojichaMochi
                      : MascotKuroRadio.IsChecked == true ? MochiDock.Core.MascotChoice.Kuro
                      : MascotShiroRadio.IsChecked == true ? MochiDock.Core.MascotChoice.Shiro
                      : MochiDock.Core.MascotChoice.Auto;
        ViewModel?.ApplyMascotChoice(chosen);
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isLoadingPreferences) return;
        string chosen = ThemeMatchaRadio.IsChecked == true ? MochiDock.Core.ThemePolicy.Matcha
                      : ThemeHojichaRadio.IsChecked == true ? MochiDock.Core.ThemePolicy.Hojicha
                      : ThemeKuroNightRadio.IsChecked == true ? MochiDock.Core.ThemePolicy.KuroNight
                      : MochiDock.Core.ThemePolicy.Default;
        ViewModel?.UpdateSettings(x => x.CapsuleTheme = chosen, SettingsApply.Theme);
    }

    private void UpdateQuickScaleText()
    {
        if (ViewModel is not { } vm) return;
        QuickScaleText.Text = vm.IsInterfaceScaleLimited ? $"{vm.InterfaceScalePercent}% (max {vm.EffectiveInterfaceScalePercent}%)" : $"{vm.InterfaceScalePercent}%";
        QuickScaleText.ToolTip = vm.IsInterfaceScaleLimited ? $"Limited to {vm.EffectiveInterfaceScalePercent}% on this display." : null;
    }

    private async void UpdateChip_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        // Checking gets the usual refresh feedback on the button; install and release page act directly.
        if (vm.UpdateChipState == MochiDock.Core.UpdateChipState.Current && sender is System.Windows.Controls.Button button)
            await MochiDock.Controls.RefreshFeedback.RunAsync(button, () => vm.CheckForUpdatesAsync(manual: true));
        else await vm.UpdateChipClickAsync();
    }

    private void ScaleDown_Click(object sender, RoutedEventArgs e) => ViewModel?.StepInterfaceScale(-1);
    private void ScaleUp_Click(object sender, RoutedEventArgs e) => ViewModel?.StepInterfaceScale(1);

    private void DockStyle_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isLoadingPreferences) return;
        string chosen = DockNotchRadio.IsChecked == true ? "Notch" : "Floating";
        ViewModel?.UpdateSettings(x => x.DockStyle = chosen, SettingsApply.Reposition);
    }

    private void NotificationSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isLoadingPreferences) return;
        bool muteEmail = MuteEmailCheck.IsChecked == true;
        bool muteDiscord = MuteDiscordCheck.IsChecked == true;
        bool muteWhatsApp = MuteWhatsAppCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x =>
        {
            x.MuteEmail = muteEmail;
            x.MuteDiscord = muteDiscord;
            x.MuteWhatsApp = muteWhatsApp;
        }, SettingsApply.CapsuleRefresh);
    }

    private void QuickSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isLoadingPreferences) return;
        bool dnd = GamingDndCheck.IsChecked == true, reminders = RemindersCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => { x.EnableGamingDnd = dnd; x.EnableAgendaReminders = reminders; }, SettingsApply.Reminders | SettingsApply.CapsuleRefresh);
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.SnoozeNotifications(TimeSpan.FromHours(1));
        UpdateSnoozeUi();
    }

    private void SnoozeLong_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.SnoozeNotifications(TimeSpan.FromHours(8));
        UpdateSnoozeUi();
    }

    private void ClearSnooze_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.SnoozeNotifications(TimeSpan.Zero);
        UpdateSnoozeUi();
    }

    private async void RetryIntegrations_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is System.Windows.Controls.Button button) await MochiDock.Controls.RefreshFeedback.RunAsync(button, vm.RetryIntegrations);
    }

    private void BackFromSettings_Click(object sender, RoutedEventArgs e) => ViewModel?.ShowMedia();

    private void OpenAllSettings_Click(object sender, RoutedEventArgs e) => App.OpenSettings();

    private void OpenNotificationsSettings_Click(object sender, RoutedEventArgs e) => App.OpenSettings(MochiDock.Core.SettingsSections.Notifications);

    private void OpenAppearanceSettings_Click(object sender, RoutedEventArgs e) => App.OpenSettings(MochiDock.Core.SettingsSections.Appearance);

    private void OpenMascotSettings_Click(object sender, RoutedEventArgs e) => App.OpenSettings(MochiDock.Core.SettingsSections.Mascot);

    private void OpenTabsSettings_Click(object sender, RoutedEventArgs e) => App.OpenSettings(MochiDock.Core.SettingsSections.Tabs);
}
