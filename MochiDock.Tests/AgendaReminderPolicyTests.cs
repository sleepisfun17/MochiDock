using MochiDock.Core;

public class AgendaReminderPolicyTests
{
    static readonly DateTime Now = new(2026, 11, 14, 9, 0, 0);

    static AgendaEvent Event(string uid, string title, DateTime start, bool allDay = false) =>
        new(uid, title, start, start.AddHours(1), allDay, "", null);

    static IReadOnlyList<AgendaReminder> Due(IEnumerable<AgendaEvent>? events = null, IEnumerable<ReminderTask>? tasks = null, int lead = 10, HashSet<string>? shown = null, DateTime? now = null) =>
        AgendaReminderPolicy.Due(events ?? Array.Empty<AgendaEvent>(), tasks ?? Array.Empty<ReminderTask>(), now ?? Now, lead, shown ?? new HashSet<string>());

    [Fact]
    public void EventIsDueFromLeadTimeUntilItsStart()
    {
        var e = new[] { Event("a", "Standup", Now.AddMinutes(10)) };
        Assert.Single(Due(e));                                     // exactly at the lead moment
        Assert.Empty(Due(e, now: Now.AddSeconds(-1)));              // just before
        Assert.Single(Due(e, now: Now.AddMinutes(9)));
        Assert.Equal("Standup now", Due(e, now: Now.AddMinutes(10))[0].Text);   // at the start the "now" reminder takes over
        Assert.Empty(Due(e, now: Now.AddMinutes(30)));              // a missed reminder is not replayed
    }

    [Fact]
    public void TextCountsMinutesUp()
    {
        var e = new[] { Event("a", "Standup", Now.AddMinutes(10)) };
        Assert.Equal("Standup in 10 min", Due(e)[0].Text);
        Assert.Equal("Standup in 1 min", Due(e, now: Now.AddMinutes(9).AddSeconds(30))[0].Text);
        var t = new[] { new ReminderTask("t1", "Send report", Now.AddMinutes(5), false) };
        Assert.Equal("Task: Send report in 5 min", Due(tasks: t)[0].Text);
    }

    [Fact]
    public void AllDayDoneAndUntimedItemsAreSkipped()
    {
        var events = new[] { Event("a", "Holiday", Now.AddMinutes(5), allDay: true) };
        var tasks = new[]
        {
            new ReminderTask("t1", "Done", Now.AddMinutes(5), true),
            new ReminderTask("t2", "No time", null, false),
        };
        Assert.Empty(Due(events, tasks));
    }

    [Fact]
    public void ShownKeysSuppressRepeatsButAMovedEventRemindsAgain()
    {
        var shown = new HashSet<string>();
        var first = Due(new[] { Event("a", "Standup", Now.AddMinutes(8)) }, shown: shown);
        Assert.Single(first);
        shown.Add(first[0].Key);
        Assert.Empty(Due(new[] { Event("a", "Standup", Now.AddMinutes(8)) }, shown: shown));
        Assert.Single(Due(new[] { Event("a", "Standup", Now.AddMinutes(9)) }, shown: shown)); // moved: new key
    }

    [Fact]
    public void ZeroLeadRemindsInTheMinuteFromTheStart()
    {
        var e = new[] { Event("a", "Standup", Now) };
        Assert.Equal("Standup now", Due(e, lead: 0)[0].Text);
        Assert.Empty(Due(e, lead: 0, now: Now.AddSeconds(-1)));
        Assert.Empty(Due(e, lead: 0, now: Now.AddMinutes(1)));
    }

    [Fact]
    public void TaskCreatedInsideTheWindowRemindsAtOnce()
    {
        var t = new[] { new ReminderTask("t1", "Quick", Now.AddMinutes(3), false) };
        Assert.Single(Due(tasks: t));
    }

    [Fact]
    public void ResultsAreOrderedByStartAndLongTitlesAreShortened()
    {
        var e = new[]
        {
            Event("b", new string('x', 80), Now.AddMinutes(9)),
            Event("a", "Earlier", Now.AddMinutes(5)),
        };
        var due = Due(e);
        Assert.Equal("Earlier in 5 min", due[0].Text);
        Assert.True(due[1].Text.Length < 60);
        Assert.Contains("…", due[1].Text);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(30, 30)]
    [InlineData(7, 10)]
    [InlineData(-1, 10)]
    public void LeadIsLimitedToTheChoices(int input, int expected) =>
        Assert.Equal(expected, AgendaReminderPolicy.NormalizeLead(input));

    [Fact]
    public void BlockedByNamesTheFirstReasonAndIsNullWhenFree()
    {
        Assert.Null(AgendaReminderPolicy.BlockedBy(false, false, false, false, false, false));
        Assert.Equal("snooze or quiet hours", AgendaReminderPolicy.BlockedBy(true, true, true, true, true, true));
        Assert.Equal("the dock is hidden (fullscreen app)", AgendaReminderPolicy.BlockedBy(true, false, false, false, false, false));
        Assert.Equal("the PC is locked", AgendaReminderPolicy.BlockedBy(false, true, false, false, false, false));
        Assert.Equal("the display is off", AgendaReminderPolicy.BlockedBy(false, false, true, false, false, false));
        Assert.Equal("another notification is showing", AgendaReminderPolicy.BlockedBy(false, false, false, true, false, false));
        Assert.Equal("a prayer reminder is showing", AgendaReminderPolicy.BlockedBy(false, false, false, false, true, false));
    }

    [Fact]
    public void ABlockedReminderStaysDueUntilItIsMarkedShown()
    {
        var e = new[] { Event("a", "Standup", Now.AddMinutes(10)) };
        var shown = new HashSet<string>();
        Assert.Single(Due(e, shown: shown, now: Now.AddMinutes(2)));      // blocked at this check: nothing is added to shown
        Assert.Single(Due(e, shown: shown, now: Now.AddMinutes(4)));      // so the next check still finds it
        shown.Add(Due(e, shown: shown, now: Now.AddMinutes(5))[0].Key);  // shown once the blocker is gone
        Assert.Empty(Due(e, shown: shown, now: Now.AddMinutes(6)));
    }

    static ReminderTask Task(DateTime due, bool done = false) => new("t1", "Send report", due, done);

    [Theory]
    [InlineData(-11, null)]
    [InlineData(-10, "Before")]
    [InlineData(-0.5, "Before")]
    [InlineData(0, "Now")]
    [InlineData(5, "Now")]
    [InlineData(9.9, "Now")]
    [InlineData(10, "Overdue10")]
    [InlineData(29, "Overdue10")]
    [InlineData(30, "Overdue30")]
    [InlineData(59, "Overdue30")]
    [InlineData(60, null)]
    public void TaskStagesFollowTheDueTime(double minutesFromDue, string? expected)
    {
        var stage = AgendaReminderPolicy.CurrentStage(Now.AddMinutes(minutesFromDue), Now, 10, isTask: true, remindOverdue: true);
        Assert.Equal(expected, stage?.ToString());
    }

    [Fact]
    public void AnEventHasOnlyBeforeAndNow()
    {
        Assert.Equal(ReminderStage.Before, AgendaReminderPolicy.CurrentStage(Now.AddMinutes(-5), Now, 10, isTask: false, remindOverdue: true));
        Assert.Equal(ReminderStage.Now, AgendaReminderPolicy.CurrentStage(Now.AddMinutes(2), Now, 10, isTask: false, remindOverdue: true));
        Assert.Null(AgendaReminderPolicy.CurrentStage(Now.AddMinutes(10), Now, 10, isTask: false, remindOverdue: true));
        Assert.Null(AgendaReminderPolicy.CurrentStage(Now.AddMinutes(30), Now, 10, isTask: false, remindOverdue: true));   // an event that started is never overdue
    }

    [Fact]
    public void ALeadOfZeroHasNoSecondNowReminder()
    {
        Assert.Equal(ReminderStage.Before, AgendaReminderPolicy.CurrentStage(Now.AddSeconds(30), Now, 0, isTask: true, remindOverdue: true));
        Assert.Null(AgendaReminderPolicy.CurrentStage(Now.AddMinutes(5), Now, 0, isTask: true, remindOverdue: true));
        Assert.Equal(ReminderStage.Overdue10, AgendaReminderPolicy.CurrentStage(Now.AddMinutes(10), Now, 0, isTask: true, remindOverdue: true));
        Assert.Null(AgendaReminderPolicy.CurrentStage(Now.AddMinutes(5), Now, 0, isTask: false, remindOverdue: true));
    }

    [Fact]
    public void AnOpenTaskIsRemindedAgainAndTheTextSaysHowLate()
    {
        var t = new[] { Task(Now) };
        Assert.Equal("Task: Send report is due now", Due(tasks: t, now: Now.AddSeconds(20))[0].Text);
        var tenMinutes = Due(tasks: t, now: Now.AddMinutes(10))[0];
        Assert.Equal("Task: Send report is overdue (10 min)", tenMinutes.Text);
        Assert.Equal(ReminderStage.Overdue10, tenMinutes.Stage);
        Assert.Equal("Task: Send report is overdue (31 min)", Due(tasks: t, now: Now.AddMinutes(31))[0].Text);
        Assert.Empty(Due(tasks: t, now: Now.AddMinutes(61)));          // after the last nudge the Today card takes over
    }

    [Fact]
    public void EachStageHasItsOwnKeyAndBeforeKeepsTheOldKey()
    {
        var t = new[] { Task(Now) };
        var before = Due(tasks: t, now: Now.AddMinutes(-5))[0];
        var now = Due(tasks: t, now: Now.AddMinutes(1))[0];
        var late = Due(tasks: t, now: Now.AddMinutes(12))[0];
        Assert.Equal(before.ItemKey, before.Key);
        Assert.Equal(3, new[] { before.Key, now.Key, late.Key }.Distinct().Count());
        Assert.Equal(before.ItemKey, late.ItemKey);
        Assert.Single(Due(tasks: t, now: Now.AddMinutes(12), shown: new HashSet<string> { before.Key, now.Key }));    // a shown stage does not block the next one
        Assert.Empty(Due(tasks: t, now: Now.AddMinutes(12), shown: new HashSet<string> { late.Key }));
    }

    [Fact]
    public void AcknowledgingAnItemStopsEveryLaterStage()
    {
        var t = new[] { Task(Now) };
        string item = Due(tasks: t, now: Now.AddMinutes(-3))[0].ItemKey;
        var acknowledged = new HashSet<string> { item };
        Assert.Empty(AgendaReminderPolicy.Due(Array.Empty<AgendaEvent>(), t, Now.AddMinutes(-3), 10, new HashSet<string>(), acknowledged));
        Assert.Empty(AgendaReminderPolicy.Due(Array.Empty<AgendaEvent>(), t, Now.AddMinutes(12), 10, new HashSet<string>(), acknowledged));
        Assert.Single(AgendaReminderPolicy.Due(Array.Empty<AgendaEvent>(), new[] { new ReminderTask("other", "Other", Now, false) }, Now.AddMinutes(12), 10, new HashSet<string>(), acknowledged));
    }

    [Fact]
    public void ADoneTaskIsNeverRemindedAgain() => Assert.Empty(Due(tasks: new[] { Task(Now, done: true) }, now: Now.AddMinutes(12)));

    [Fact]
    public void WithOverdueRemindersOffATaskGetsOnlyTheEarlyReminder()
    {
        var t = new[] { Task(Now) };
        Assert.Single(AgendaReminderPolicy.Due(Array.Empty<AgendaEvent>(), t, Now.AddMinutes(-5), 10, new HashSet<string>(), null, remindOverdue: false));
        Assert.Empty(AgendaReminderPolicy.Due(Array.Empty<AgendaEvent>(), t, Now.AddMinutes(1), 10, new HashSet<string>(), null, remindOverdue: false));
        Assert.Empty(AgendaReminderPolicy.Due(Array.Empty<AgendaEvent>(), t, Now.AddMinutes(12), 10, new HashSet<string>(), null, remindOverdue: false));
        Assert.Single(AgendaReminderPolicy.Due(new[] { Event("a", "Standup", Now) }, Array.Empty<ReminderTask>(), Now.AddMinutes(1), 10, new HashSet<string>(), null, remindOverdue: false));   // events keep their now reminder
    }

    [Theory]
    [InlineData(600, "in 10m")]
    [InlineData(30, "in 1m")]
    [InlineData(3900, "in 1h 05m")]
    [InlineData(-10, "Due now")]
    [InlineData(-59, "Due now")]
    [InlineData(-60, "Overdue 1m")]
    [InlineData(-1500, "Overdue 25m")]
    [InlineData(-7500, "Overdue 2h 05m")]
    [InlineData(-260000, "Overdue 3d")]
    public void TaskStatusText(double secondsFromNow, string expected) =>
        Assert.Equal(expected, AgendaLogic.TaskStatus(Now.AddSeconds(secondsFromNow), false, Now));

    [Fact]
    public void TaskStatusIsEmptyForDoneOrUntimedTasksAndOverdueNeedsAMinute()
    {
        Assert.Equal("", AgendaLogic.TaskStatus(Now, true, Now));
        Assert.Equal("", AgendaLogic.TaskStatus(null, false, Now));
        Assert.False(AgendaLogic.IsOverdue(Now.AddSeconds(-30), false, Now));
        Assert.True(AgendaLogic.IsOverdue(Now.AddMinutes(-2), false, Now));
        Assert.False(AgendaLogic.IsOverdue(Now.AddMinutes(-2), true, Now));
    }

    [Fact]
    public void MultiCalendarRemindersPrefixCalendarNameWhenEnabledAndDeduplicateByUid()
    {
        var start = Now.AddMinutes(5);
        var shared1 = new AgendaEvent("shared-1", "Design Sync", start, start.AddHours(1), false, "", null, "Work", "Blue");
        var shared2 = new AgendaEvent("shared-1", "Design Sync", start, start.AddHours(1), false, "", null, "Personal", "Green");
        var solo = new AgendaEvent("p-2", "Dentist", Now.AddMinutes(8), Now.AddMinutes(68), false, "", null, "Personal", "Green");

        var singleCal = AgendaReminderPolicy.Due(new[] { solo }, Array.Empty<ReminderTask>(), Now, 10, new HashSet<string>(), includeCalendarName: false);
        Assert.Equal("Dentist in 8 min", singleCal[0].Text);

        var shown = new HashSet<string>();
        var first = AgendaReminderPolicy.Due(new[] { shared1 }, Array.Empty<ReminderTask>(), Now, 10, shown, includeCalendarName: true);
        Assert.Equal("Work · Design Sync in 5 min", first[0].Text);
        shown.Add(first[0].Key);

        // Same UID + start from another calendar doesn't double-remind.
        Assert.Empty(AgendaReminderPolicy.Due(new[] { shared1, shared2 }, Array.Empty<ReminderTask>(), Now, 10, shown, includeCalendarName: true));
    }
}
