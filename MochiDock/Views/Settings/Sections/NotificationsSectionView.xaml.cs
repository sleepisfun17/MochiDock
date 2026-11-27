using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MochiDock.Core;
using MochiDock.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;
using ComboBox = System.Windows.Controls.ComboBox;

namespace MochiDock.Views.Settings.Sections;

/// <summary>Notification settings. Writes go through <see cref="MainViewModel.UpdateSettings"/>; sounds and power alerts bind to the view model.</summary>
public partial class NotificationsSectionView : SettingsSectionBase
{
    private readonly DispatcherTimer _snoozeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private MainViewModel? _subscribedVm;

    public NotificationsSectionView()
    {
        InitializeComponent();
        _snoozeTimer.Tick += (s, e) => UpdateSnoozeText();
        Loaded += (s, e) => { _snoozeTimer.Start(); SubscribeVm(); };
        Unloaded += (s, e) => { _snoozeTimer.Stop(); UnsubscribeVm(); };
    }

    private void SubscribeVm()
    {
        if (ReferenceEquals(_subscribedVm, ViewModel)) return;
        UnsubscribeVm();
        _subscribedVm = ViewModel;
        if (_subscribedVm != null) _subscribedVm.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void UnsubscribeVm()
    {
        if (_subscribedVm != null) _subscribedVm.PropertyChanged -= ViewModel_PropertyChanged;
        _subscribedVm = null;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.UnwatchedRecentApps))
            RefreshRecentAppsCombo();
        else if (e.PropertyName == nameof(MainViewModel.NotificationAccessAllowed))
            NotificationAccessText.Visibility = ViewModel?.NotificationAccessAllowed == false ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshRecentAppsCombo()
    {
        if (ViewModel == null) return;
        var selected = RecentAppsCombo.SelectedItem as WatchedApp;
        var items = ViewModel.UnwatchedRecentApps;
        RecentAppsCombo.ItemsSource = items;
        if (selected != null)
        {
            var match = items.FirstOrDefault(r => AppNotificationFilter.Same(r, selected));
            if (match != null) { RecentAppsCombo.SelectedItem = match; return; }
        }
        if (RecentAppsCombo.SelectedIndex < 0 && items.Count > 0) RecentAppsCombo.SelectedIndex = 0;
    }

    private void UpdateWatchAllUi(bool watchAll)
    {
        AppsAddRow.IsEnabled = !watchAll;
        AppsAddRow.Opacity = watchAll ? 0.5 : 1.0;
        WatchedAppsList.IsEnabled = !watchAll;
        WatchedAppsList.Opacity = watchAll ? 0.5 : 1.0;
    }

    private static void SetText(TextBox box, string text) { if (!box.IsKeyboardFocused) box.Text = text; }

    protected override void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        var settings = vm.Settings;
        {
            MuteEmailCheck.IsChecked = settings.MuteEmail;
            MuteDiscordCheck.IsChecked = settings.MuteDiscord;
            MuteAppsCheck.IsChecked = settings.MuteApps;
            WatchAllAppsCheck.IsChecked = settings.WatchAllApps;
            UpdateWatchAllUi(settings.WatchAllApps);
            WatchedAppsList.ItemsSource = vm.WatchedApps.ToList();
            RefreshRecentAppsCombo();
            NotificationAccessText.Visibility = vm.NotificationAccessAllowed == false ? Visibility.Visible : Visibility.Collapsed;
            MuteWhatsAppCheck.IsChecked = settings.MuteWhatsApp;
            QuietHoursCheck.IsChecked = settings.QuietHoursEnabled;
            SetText(QuietStartBox, settings.QuietHoursStart ?? "");
            SetText(QuietEndBox, settings.QuietHoursEnd ?? "");
            MinimalNoticeCheck.IsChecked = !settings.AutoExpandNotificationsOnArrival;
            GamingDndCheck.IsChecked = settings.EnableGamingDnd;
            OvernightGreetingCheck.IsChecked = settings.EnableOvernightGreeting;
            RemindersCheck.IsChecked = settings.EnableAgendaReminders;
            SelectLead(AgendaReminderPolicy.NormalizeLead(settings.AgendaReminderLeadMinutes));
            LeadCombo.IsEnabled = settings.EnableAgendaReminders;
            HiddenFallbackCombo.IsEnabled = settings.EnableAgendaReminders;
            OverdueCheck.IsChecked = settings.RemindOverdueTasks;
            OverdueCheck.IsEnabled = settings.EnableAgendaReminders;
            foreach (ComboBoxItem item in HiddenFallbackCombo.Items)
                if ((item.Tag as string) == ReminderFallbackPolicy.NormalizeMode(settings.HiddenReminderFallback)) { HiddenFallbackCombo.SelectedItem = item; break; }
            AllowList.ItemsSource = settings.GamingAllowList.ToList();
        }
        UpdateSnoozeText();
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        bool muteEmail = MuteEmailCheck.IsChecked == true, muteDiscord = MuteDiscordCheck.IsChecked == true, muteApps = MuteAppsCheck.IsChecked == true, muteWhatsApp = MuteWhatsAppCheck.IsChecked == true;
        bool quiet = QuietHoursCheck.IsChecked == true, autoExpand = MinimalNoticeCheck.IsChecked != true;
        bool dnd = GamingDndCheck.IsChecked == true, greeting = OvernightGreetingCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x =>
        {
            x.MuteEmail = muteEmail;
            x.MuteDiscord = muteDiscord;
            x.MuteApps = muteApps;
            x.MuteWhatsApp = muteWhatsApp;
            x.QuietHoursEnabled = quiet;
            x.AutoExpandNotificationsOnArrival = autoExpand;
            x.EnableGamingDnd = dnd;
            x.EnableOvernightGreeting = greeting;
        }, SettingsApply.CapsuleRefresh);
    }

    private void SelectLead(int minutes)
    {
        foreach (ComboBoxItem item in LeadCombo.Items)
            if ((item.Tag as string) == minutes.ToString()) { LeadCombo.SelectedItem = item; return; }
        LeadCombo.SelectedIndex = 2;
    }

    private void Reminders_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        bool on = RemindersCheck.IsChecked == true;
        LeadCombo.IsEnabled = on;
        HiddenFallbackCombo.IsEnabled = on;
        OverdueCheck.IsEnabled = on;
        ViewModel?.UpdateSettings(x => x.EnableAgendaReminders = on, SettingsApply.Reminders);
    }

    private void Lead_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Loading || ViewModel == null) return;
        if (LeadCombo.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int minutes))
        {
            int lead = AgendaReminderPolicy.NormalizeLead(minutes);
            if (lead != ViewModel.Settings.AgendaReminderLeadMinutes)
                ViewModel.UpdateSettings(x => x.AgendaReminderLeadMinutes = lead, SettingsApply.Reminders);
        }
    }

    private void Overdue_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        bool on = OverdueCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => x.RemindOverdueTasks = on);
    }

    private void HiddenFallback_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Loading || ViewModel == null) return;
        if (HiddenFallbackCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            string mode = ReminderFallbackPolicy.NormalizeMode(tag);
            if (mode != ReminderFallbackPolicy.NormalizeMode(ViewModel.Settings.HiddenReminderFallback)) ViewModel.UpdateSettings(x => x.HiddenReminderFallback = mode);
        }
    }

    private void AllowWhatsApp_Click(object sender, RoutedEventArgs e) => AllowAddRow.Validation = ViewModel?.AllowLastWhatsAppWhileGaming() ?? "";

    private void AllowDiscord_Click(object sender, RoutedEventArgs e)
    {
        string message = ViewModel?.AllowDiscordWhileGaming(AllowDiscordBox.Text) ?? "";
        AllowAddRow.Validation = message;
        if (message.Length == 0) AllowDiscordBox.Text = "";
    }

    private void AllowDiscord_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        AllowDiscord_Click(sender, e);
        e.Handled = true;
    }

    private void WatchAllApps_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        bool on = WatchAllAppsCheck.IsChecked == true;
        ViewModel?.SetWatchAllApps(on);
        UpdateWatchAllUi(on);
    }

    private void AddRecentApp_Click(object sender, RoutedEventArgs e)
    {
        if (RecentAppsCombo.SelectedItem is not WatchedApp app)
        {
            AppsAddRow.Validation = "No other app has sent a notification since MochiDock started. Type its name instead.";
            return;
        }
        AppsAddRow.Validation = "";
        ViewModel?.WatchApp(app);
        LoadFromSettings();
    }

    private void AddAppByName_Click(object sender, RoutedEventArgs e)
    {
        string name = AppNameBox.Text.Trim();
        if (name.Length == 0) { AppsAddRow.Validation = "Type the app's name as Windows shows it on its notifications."; return; }
        if (AppNotificationFilter.IsDiscord(name, "")) { AppsAddRow.Validation = "Discord already has its own card."; return; }
        AppsAddRow.Validation = "";
        ViewModel?.WatchApp(new WatchedApp(name, ""));
        AppNameBox.Text = "";
        LoadFromSettings();
    }

    private void AppName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        AddAppByName_Click(sender, e);
        e.Handled = true;
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WatchedApp app }) ViewModel?.UnwatchApp(app);
        LoadFromSettings();
    }

    private void RemoveAllow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GamingAllowEntry entry }) ViewModel?.RemoveGamingAllow(entry);
    }

    private void Quiet_LostFocus(object sender, RoutedEventArgs e) => CommitQuietHours();

    private void Quiet_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        CommitQuietHours();
        e.Handled = true;
    }

    // Each time is committed on its own so one bad value never blocks the other; an invalid one keeps its last good value.
    private void CommitQuietHours()
    {
        if (!IsLoaded || Loading || ViewModel == null) return;
        var settings = ViewModel.Settings;
        string start = settings.QuietHoursStart, end = settings.QuietHoursEnd;
        QuietStartRow.Validation = Normalize(QuietStartBox, QuietStartRow, ref start, settings.QuietHoursStart);
        QuietEndRow.Validation = Normalize(QuietEndBox, QuietEndRow, ref end, settings.QuietHoursEnd);
        if (start == settings.QuietHoursStart && end == settings.QuietHoursEnd) return;
        ViewModel.UpdateSettings(x => { x.QuietHoursStart = start; x.QuietHoursEnd = end; }, SettingsApply.None);
    }

    private static string Normalize(TextBox box, SettingRow row, ref string value, string lastGood)
    {
        if (!SettingsValidation.TryParseTimeOfDay(box.Text, out var time))
            return $"Use a 24-hour time such as 22:00, 2200 or 22. Keeping {lastGood}.";
        value = MochiDock.Core.TimeInput.Format(time);
        box.Text = value;
        return "";
    }

    private void Snooze1_Click(object sender, RoutedEventArgs e) => Snooze(TimeSpan.FromHours(1));
    private void Snooze8_Click(object sender, RoutedEventArgs e) => Snooze(TimeSpan.FromHours(8));
    private void ClearSnooze_Click(object sender, RoutedEventArgs e) => Snooze(TimeSpan.Zero);

    private void Snooze(TimeSpan duration)
    {
        ViewModel?.SnoozeNotifications(duration);
        UpdateSnoozeText();
    }

    private void UpdateSnoozeText()
    {
        long ticks = ViewModel?.Settings.SnoozeUntilTicks ?? 0;
        bool active = ticks > DateTime.Now.Ticks;
        ClearSnoozeBtn.IsEnabled = active;
        if (!active) { SnoozeStatusText.Text = "Not snoozed."; return; }
        var until = new DateTime(ticks);
        SnoozeStatusText.Text = $"Snoozed until {until:HH:mm} ({Math.Ceiling((until - DateTime.Now).TotalMinutes)} min left)";
    }
}
