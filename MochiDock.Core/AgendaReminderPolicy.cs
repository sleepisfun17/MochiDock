using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>A task as the reminder policy sees it.</summary>
public sealed record ReminderTask(string Id, string Title, DateTime? Due, bool Done);

/// <summary>A reminder that should be shown now. <see cref="Key"/> identifies the item and its time so it is shown only once.</summary>
public sealed record AgendaReminder(string Key, string Text, DateTime Start, string ItemKey = "", ReminderStage Stage = ReminderStage.Before);

/// <summary>The moments at which one item is reminded: before its start, at its start, and (tasks only) again when it stays undone.</summary>
public enum ReminderStage { Before, Now, Overdue10, Overdue30 }

/// <summary>Decides which calendar events and timed tasks need a reminder right now. Pure, so the rules are unit-tested.</summary>
public static class AgendaReminderPolicy
{
    public static readonly int[] LeadChoices = { 0, 5, 10, 15, 30 };
    public const int DefaultLeadMinutes = 10;
    const int MaxTitle = 36;

    /// <summary>The lead time if it is one of <see cref="LeadChoices"/>, else the default.</summary>
    public static int NormalizeLead(int minutes) => Array.IndexOf(LeadChoices, minutes) >= 0 ? minutes : DefaultLeadMinutes;

    /// <summary>
    /// Items whose reminder moment has come. Events are reminded <see cref="ReminderStage.Before"/> their start (from <c>start - lead</c>) and
    /// once more at the start (<see cref="ReminderStage.Now"/>, within ten minutes). Timed tasks are reminded the same way and, when
    /// <paramref name="remindOverdue"/> is on, again 10 and 30 minutes after the due time while they are still open; that is the end.
    /// All-day events, completed tasks, tasks without a due time, anything in <paramref name="acknowledged"/> (an item key) and any
    /// reminder already in <paramref name="shown"/> are skipped. Only the stage that is current now is considered, so a stage that was
    /// missed (PC asleep, app closed, blocked until the next stage began) is never replayed.
    /// </summary>
    public static IReadOnlyList<AgendaReminder> Due(IEnumerable<AgendaEvent> events, IEnumerable<ReminderTask> tasks, DateTime now, int leadMinutes, ISet<string> shown,
        ISet<string>? acknowledged = null, bool remindOverdue = true, bool includeCalendarName = false)
    {
        int lead = NormalizeLead(leadMinutes);
        var due = new List<AgendaReminder>();

        foreach (var e in events)
        {
            if (e.AllDay) continue;
            string rawTitle = includeCalendarName && !string.IsNullOrWhiteSpace(e.CalendarName)
                ? $"{e.CalendarName.Trim()} · {e.Title}"
                : e.Title;
            Consider($"ev:{e.Uid}|{e.Start:O}", Shorten(rawTitle), e.Start, isTask: false);
        }
        foreach (var t in tasks)
        {
            if (t.Done || t.Due is not { } at) continue;
            Consider(TaskItemKey(t.Id, at), Shorten(t.Title), at, isTask: true);
        }
        return due.OrderBy(r => r.Start).ToList();

        void Consider(string itemKey, string title, DateTime start, bool isTask)
        {
            if (acknowledged != null && acknowledged.Contains(itemKey)) return;
            var stage = CurrentStage(now, start, lead, isTask, remindOverdue);
            if (stage is not { } s) return;
            string key = s == ReminderStage.Before ? itemKey : itemKey + "#" + s.ToString().ToLowerInvariant();
            if (shown.Contains(key)) return;
            due.Add(new AgendaReminder(key, Text(title, start, now, lead, isTask, s), start, itemKey, s));
        }
    }

    /// <summary>The key of a task as an item (what acknowledging stores), shared by the reminders and Mochi.</summary>
    public static string TaskItemKey(string id, DateTime due) => $"task:{id}|{due:O}";

    /// <summary>True when clicking a reminder should acknowledge the item and suppress later stages/overdue Angry. Clicking an early "in X min" heads-up only opens Today.</summary>
    public static bool ShouldAcknowledgeOnClick(ReminderStage stage) => stage != ReminderStage.Before;

    /// <summary>The stage whose window contains <paramref name="now"/>, or null between stages and after the last one.</summary>
    public static ReminderStage? CurrentStage(DateTime now, DateTime start, int leadMinutes, bool isTask, bool remindOverdue)
    {
        int lead = NormalizeLead(leadMinutes);
        var elapsed = now - start;
        // With a lead of 0 the "before" moment is the start itself, so the first window already is the "now" one.
        if (lead > 0 && now >= start.AddMinutes(-lead) && now < start) return ReminderStage.Before;
        if (elapsed < TimeSpan.Zero) return null;
        if (lead == 0 && elapsed < TimeSpan.FromMinutes(1)) return ReminderStage.Before;
        if (isTask && !remindOverdue) return null;
        if (elapsed < TimeSpan.FromMinutes(10)) return lead > 0 ? ReminderStage.Now : null;   // lead 0: the first window already was the "now" one
        if (!isTask) return null;
        if (elapsed < TimeSpan.FromMinutes(30)) return ReminderStage.Overdue10;
        if (elapsed < TimeSpan.FromMinutes(60)) return ReminderStage.Overdue30;
        return null;
    }

    /// <summary>
    /// Why a due reminder cannot be shown at this moment, or null when it can. A reminder that is blocked is not marked as shown:
    /// it is tried again at the next check for as long as its window lasts, so it appears as soon as the blocker is gone.
    /// </summary>
    /// <summary>The <see cref="BlockedBy"/> reason for a dock that a fullscreen app hides; the only one the hidden-dock fallback answers.</summary>
    public const string DockHiddenReason = "the dock is hidden (fullscreen app)";

    public static string? BlockedBy(bool dockHidden, bool locked, bool displayOff, bool notificationShowing, bool prayerShowing, bool snoozedOrQuiet)
    {
        if (snoozedOrQuiet) return "snooze or quiet hours";
        if (dockHidden) return DockHiddenReason;
        if (locked) return "the PC is locked";
        if (displayOff) return "the display is off";
        if (notificationShowing) return "another notification is showing";
        if (prayerShowing) return "a prayer reminder is showing";
        return null;
    }

    static string Text(string title, DateTime start, DateTime now, int lead, bool isTask, ReminderStage stage)
    {
        string prefix = isTask ? "Task: " : "";
        if (stage == ReminderStage.Before && lead > 0)
        {
            int minutes = Math.Max(1, (int)Math.Ceiling((start - now).TotalMinutes));
            return $"{prefix}{title} in {minutes} min";
        }
        int late = (int)(now - start).TotalMinutes;
        if (isTask && stage != ReminderStage.Before && late >= 2) return $"{prefix}{title} is overdue ({late} min)";
        return isTask && stage != ReminderStage.Before ? $"{prefix}{title} is due now" : $"{prefix}{title} now";
    }

    static string Shorten(string? title)
    {
        string t = (title ?? "").Trim();
        if (t.Length == 0) return "Untitled";
        return t.Length <= MaxTitle ? t : t[..(MaxTitle - 1)].TrimEnd() + "…";
    }
}
