using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.ViewModels;

public partial class MainViewModel
{
    // Reminder for a calendar event or a task with a due time. It reuses the capsule notice slot of the hardware and AI alerts
    // (8 seconds, a click opens the Today tab) and never expands the dock by itself.
    private bool _noticeIsAgenda;
    private readonly HashSet<string> _shownReminders = new();
    private DispatcherTimer? _reminderTimer;

    public void ShowToday(bool rememberSelection = true)
    {
        if (rememberSelection && ShowTodayTab) NavigateManually(IslandCardType.Today); else ActiveCardType = IslandCardType.Today;
        IsExpanded = true;
    }

    /// <summary>
    /// Starts or stops the reminder check and the calendar's background refresh to match <c>EnableAgendaReminders</c>.
    /// Reminders that are already inside their lead window are marked as shown first, so switching on or restarting never repeats one.
    /// </summary>
    public void ApplyReminderSettings()
    {
        bool on = _settings.EnableAgendaReminders;
        // The calendar also refreshes in the background for Mochi's meeting face; with no calendar link it does nothing.
        CalendarFeed?.SetBackground(true);
        UpdateOverdueMood();
        if (!on)
        {
            _reminderTimer?.Stop();
            return;
        }

        _reminderTimer ??= CreateReminderTimer();
        if (!_reminderTimer.IsEnabled)
        {
            MarkCurrentRemindersShown();
            _reminderTimer.Start();
        }
    }

    private DispatcherTimer CreateReminderTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) => CheckReminders();
        return timer;
    }

    private IReadOnlyList<AgendaReminder> DueReminders() =>
        AgendaReminderPolicy.Due(
            CalendarFeed?.Events ?? Array.Empty<AgendaEvent>(),
            Tasks.Select(t => new ReminderTask(t.Id, t.Title, t.DueTime, t.IsCompleted)).ToList(),
            DateTime.Now, _settings.AgendaReminderLeadMinutes, _shownReminders, _acknowledgedReminders, _settings.RemindOverdueTasks,
            includeCalendarName: (CalendarFeed?.Feeds.Count ?? 0) > 1);

    private void MarkCurrentRemindersShown()
    {
        foreach (var reminder in DueReminders()) _shownReminders.Add(reminder.Key);
    }

    private readonly HashSet<string> _loggedReminderBlocks = new();
    private readonly HashSet<string> _fallbackFired = new();
    private readonly HashSet<string> _acknowledgedReminders = new();
    private IReadOnlyList<string> _noticeAgendaItems = Array.Empty<string>();

    /// <summary>Asks the dock window to show a Windows notification (title, text) for a reminder the hidden dock cannot show.</summary>
    public event Action<string, string>? ReminderFallbackRequested;

    /// <summary>Clicking a due-now or overdue reminder acknowledges it (early heads-ups only open Today).</summary>
    private void AcknowledgeShownReminders()
    {
        foreach (var item in _noticeAgendaItems) if (item.Length > 0) _acknowledgedReminders.Add(item);
        _noticeAgendaItems = Array.Empty<string>();
        if (_acknowledgedReminders.Count > 1000) _acknowledgedReminders.Clear();
        UpdateOverdueMood();
    }

    private void CheckReminders()
    {
        UpdateOverdueMood();
        if (!_settings.EnableAgendaReminders) return;
        var due = DueReminders();
        if (due.Count == 0) return;

        string text = due.Count > 1 ? $"{due[0].Text} (+{due.Count - 1})" : due[0].Text;

        // A reminder is marked as shown only when it really appears. One that is blocked right now (snooze, a hidden dock, a lock,
        // another notice) is tried again at the next check for as long as its window lasts, and the reason is written to the log.
        string? blocked = AgendaReminderPolicy.BlockedBy(
            _hardwarePresentationSuppressed, _isWorkstationLocked, !_displayAwake, IsNotificationInMainPill, IsPrayerReminderVisible, !ShouldShowNotification("Agenda"));
        if (blocked != null)
        {
            string logKey = due[0].Key + "|" + blocked;
            if (_loggedReminderBlocks.Add(logKey)) AppLog.Warn("Reminder", $"Waiting to show \"{due[0].Text}\": {blocked}.");
            if (_loggedReminderBlocks.Count > 200) _loggedReminderBlocks.Clear();

            // The dock is hidden by a fullscreen app: nothing on screen can show the reminder, so give a sound and/or a Windows
            // notification once. The reminder stays pending, so the capsule still shows it if the dock returns before the start.
            var fallback = ReminderFallbackPolicy.Decide(_settings.HiddenReminderFallback, blocked, _fallbackFired.Contains(due[0].Key));
            if (fallback.Any)
            {
                _fallbackFired.Add(due[0].Key);
                if (_fallbackFired.Count > 500) _fallbackFired.Clear();
                AppLog.Warn("Reminder", $"Dock hidden, fallback for \"{text}\": sound={fallback.Sound}, notification={fallback.Notification}.");
                if (fallback.Sound) SoundService.PlayNotificationSound();   // does nothing while notification sounds are off
                if (fallback.Notification) ReminderFallbackRequested?.Invoke("MochiDock", text);
            }
            return;
        }

        foreach (var reminder in due) _shownReminders.Add(reminder.Key);
        if (_shownReminders.Count > 1000) _shownReminders.Clear();
        if (_wasGaming)
        {
            // During a game the capsule shows the readout, so the reminder is a short line there (no dock takeover).
            AppLog.Warn("Reminder", $"Shown in the gaming capsule: \"{text}\".");
            ShowGamingPassThrough(text);
            return;
        }

        AppLog.Warn("Reminder", $"Shown: \"{text}\".");
        _noticeIsAi = false;
        _noticeIsGame = false; _noticeIsDevice = false; _noticeIsWeather = false; _noticeIsUpdate = false;
        _noticeIsAgenda = true;
        _noticeAgendaItems = due.Where(r => AgendaReminderPolicy.ShouldAcknowledgeOnClick(r.Stage)).Select(r => r.ItemKey).ToList();
        _hardwareNoticeText = text;
        _hardwareNoticeUntil = DateTimeOffset.UtcNow.AddSeconds(8);
        _hardwareNoticeTimer.Stop();
        _hardwareNoticeTimer.Start();
        NotifyHardwareNotice();
        if (!_fallbackFired.Contains(due[0].Key)) SoundService.PlayNotificationSound();   // the fallback already made a sound; does nothing while sounds are off
    }
}
