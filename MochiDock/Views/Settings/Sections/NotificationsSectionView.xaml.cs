using System;
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
public partial class NotificationsSectionView : UserControl
{
    private bool _loading;
    private MainViewModel? _subscribed;
    private readonly DispatcherTimer _snoozeTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public NotificationsSectionView()
    {
        InitializeComponent();
        _snoozeTimer.Tick += (s, e) => UpdateSnoozeText();
        Loaded += (s, e) => { Subscribe(); LoadFromSettings(); _snoozeTimer.Start(); };
        Unloaded += (s, e) => { Unsubscribe(); _snoozeTimer.Stop(); };
        IsVisibleChanged += (s, e) => { if (IsVisible) LoadFromSettings(); };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

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

    private void OnSettingsChanged(SettingsApply effects) => Dispatcher.BeginInvoke(LoadFromSettings);

    private static void SetText(TextBox box, string text) { if (!box.IsKeyboardFocused) box.Text = text; }

    private void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        var settings = vm.Settings;
        _loading = true;
        try
        {
            MuteEmailCheck.IsChecked = settings.MuteEmail;
            MuteDiscordCheck.IsChecked = settings.MuteDiscord;
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
        finally { _loading = false; }
        UpdateSnoozeText();
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _loading) return;
        bool muteEmail = MuteEmailCheck.IsChecked == true, muteDiscord = MuteDiscordCheck.IsChecked == true, muteWhatsApp = MuteWhatsAppCheck.IsChecked == true;
        bool quiet = QuietHoursCheck.IsChecked == true, autoExpand = MinimalNoticeCheck.IsChecked != true;
        bool dnd = GamingDndCheck.IsChecked == true, greeting = OvernightGreetingCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x =>
        {
            x.MuteEmail = muteEmail;
            x.MuteDiscord = muteDiscord;
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
        if (!IsLoaded || _loading) return;
        bool on = RemindersCheck.IsChecked == true;
        LeadCombo.IsEnabled = on;
        HiddenFallbackCombo.IsEnabled = on;
        OverdueCheck.IsEnabled = on;
        ViewModel?.UpdateSettings(x => x.EnableAgendaReminders = on, SettingsApply.Reminders);
    }

    private void Lead_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _loading || ViewModel == null) return;
        if (LeadCombo.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int minutes))
        {
            int lead = AgendaReminderPolicy.NormalizeLead(minutes);
            if (lead != ViewModel.Settings.AgendaReminderLeadMinutes)
                ViewModel.UpdateSettings(x => x.AgendaReminderLeadMinutes = lead, SettingsApply.Reminders);
        }
    }

    private void Overdue_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _loading) return;
        bool on = OverdueCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => x.RemindOverdueTasks = on);
    }

    private void HiddenFallback_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _loading || ViewModel == null) return;
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
        if (!IsLoaded || _loading || ViewModel == null) return;
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
