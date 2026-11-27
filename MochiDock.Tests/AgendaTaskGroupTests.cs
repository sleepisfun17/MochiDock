using MochiDock.Core;

public class AgendaTaskGroupTests
{
    static readonly DateTime Today = new(2026, 11, 14);

    [Fact]
    public void TaskDueOnTheSelectedDayBelongsToThatDayWhetherDoneOrNot()
    {
        Assert.Equal(AgendaTaskGroup.OnDay, AgendaLogic.TaskGroup(Today.AddHours(15), false, Today, Today));
        Assert.Equal(AgendaTaskGroup.OnDay, AgendaLogic.TaskGroup(Today.AddHours(15), true, Today, Today));
        var other = Today.AddDays(3);
        Assert.Equal(AgendaTaskGroup.OnDay, AgendaLogic.TaskGroup(other.AddHours(9), false, other, Today));
    }

    [Fact]
    public void UndatedAndOverdueOpenTasksFloatOnTodayOnly()
    {
        Assert.Equal(AgendaTaskGroup.Floating, AgendaLogic.TaskGroup(null, false, Today, Today));
        Assert.Equal(AgendaTaskGroup.Floating, AgendaLogic.TaskGroup(Today.AddDays(-2).AddHours(10), false, Today, Today));
        Assert.Equal(AgendaTaskGroup.None, AgendaLogic.TaskGroup(null, false, Today.AddDays(1), Today));
        Assert.Equal(AgendaTaskGroup.None, AgendaLogic.TaskGroup(Today.AddDays(-2), false, Today.AddDays(-1), Today));
    }

    [Fact]
    public void CompletedTasksOnOtherDaysAndFutureTasksAreNotListed()
    {
        Assert.Equal(AgendaTaskGroup.None, AgendaLogic.TaskGroup(null, true, Today, Today));
        Assert.Equal(AgendaTaskGroup.None, AgendaLogic.TaskGroup(Today.AddDays(-2), true, Today, Today));
        Assert.Equal(AgendaTaskGroup.None, AgendaLogic.TaskGroup(Today.AddDays(2), false, Today, Today));
    }

    [Fact]
    public void ParseTaskInputAttachesUntimedAndClockTasksToSelectedDate()
    {
        var now = new DateTime(2026, 11, 27, 15, 0, 0);
        var dec5 = new DateTime(2026, 12, 5);

        // Plain untimed or "No time" on another date becomes an all-day task for that date.
        var plainFuture = AgendaLogic.ParseTaskInput("Pay rent", now, null, dec5);
        Assert.Equal(("Pay rent", dec5, true), (plainFuture.Title, plainFuture.DueTime, plainFuture.AllDay));

        var chipNoTimeFuture = AgendaLogic.ParseTaskInput("Buy ticket", now, 0, dec5);
        Assert.Equal(("Buy ticket", dec5, true), (chipNoTimeFuture.Title, chipNoTimeFuture.DueTime, chipNoTimeFuture.AllDay));

        // Clock times on another date anchor cleanly on that selected date (no rolling even if < now.TimeOfDay).
        var clockMorning = AgendaLogic.ParseTaskInput("Call clinic at 9am", now, null, dec5);
        Assert.Equal(("Call clinic", dec5.AddHours(9), false), (clockMorning.Title, clockMorning.DueTime, clockMorning.AllDay));

        var clock24 = AgendaLogic.ParseTaskInput("Dentist at 14:30", now, null, dec5);
        Assert.Equal(("Dentist", dec5.AddHours(14).AddMinutes(30), false), (clock24.Title, clock24.DueTime, clock24.AllDay));

        var clockWord = AgendaLogic.ParseTaskInput("Dinner jam 19", now, null, dec5);
        Assert.Equal(("Dinner", dec5.AddHours(19), false), (clockWord.Title, clockWord.DueTime, clockWord.AllDay));

        // Today keeps floating untimed tasks and rolls past clock times to tomorrow.
        var plainToday = AgendaLogic.ParseTaskInput("Read docs", now, null, now.Date);
        Assert.Equal(("Read docs", (DateTime?)null, false), (plainToday.Title, plainToday.DueTime, plainToday.AllDay));

        var pastToday = AgendaLogic.ParseTaskInput("Standup at 9am", now, null, now.Date);
        Assert.Equal(now.Date.AddDays(1).AddHours(9), pastToday.DueTime);
        Assert.False(pastToday.AllDay);

        // Relative durations stay relative to now regardless of selected date.
        var relText = AgendaLogic.ParseTaskInput("Check oven in 30m", now, null, dec5);
        Assert.Equal(("Check oven", now.AddMinutes(30), false), (relText.Title, relText.DueTime, relText.AllDay));

        var relChip = AgendaLogic.ParseTaskInput("Stretch", now, 15, dec5);
        Assert.Equal(("Stretch", now.AddMinutes(15), false), (relChip.Title, relChip.DueTime, relChip.AllDay));
    }

    [Fact]
    public void FormatTaskForEditIncludesTimeOnlyForTimedTasks()
    {
        var due = new DateTime(2026, 11, 27, 22, 0, 0);
        Assert.Equal("Coretax 22:00", AgendaLogic.FormatTaskForEdit("  Coretax ", due, allDay: false));
        Assert.Equal("Pay rent", AgendaLogic.FormatTaskForEdit("Pay rent", due.Date, allDay: true));
        Assert.Equal("Read docs", AgendaLogic.FormatTaskForEdit("Read docs", null, allDay: false));
    }

    [Fact]
    public void ParseTaskEditPreservesOverdueTimeOnRenameAndMovesAcrossDates()
    {
        var now = new DateTime(2026, 11, 27, 15, 0, 0);
        var earlierToday = new DateTime(2026, 11, 27, 10, 0, 0);

        // Renaming an overdue task on Today without changing its clock time keeps it on Today instead of rolling to tomorrow.
        var renamedOverdue = AgendaLogic.ParseTaskEdit("Fixed title 10:00", now, null, now.Date, earlierToday, originalAllDay: false);
        Assert.Equal(("Fixed title", earlierToday, false), (renamedOverdue.Title, renamedOverdue.DueTime, renamedOverdue.AllDay));

        // Changing to a different past clock time today still rolls to tomorrow as in quick-add.
        var retimed = AgendaLogic.ParseTaskEdit("Fixed title 09:30", now, null, now.Date, earlierToday, originalAllDay: false);
        Assert.Equal(now.Date.AddDays(1).AddHours(9).AddMinutes(30), retimed.DueTime);

        // Selecting another date moves the task to that date.
        var dec5 = new DateTime(2026, 12, 5);
        var moved = AgendaLogic.ParseTaskEdit("Fixed title 10:00", now, null, dec5, earlierToday, originalAllDay: false);
        Assert.Equal(("Fixed title", dec5.AddHours(10), false), (moved.Title, moved.DueTime, moved.AllDay));

        // Clicking "No time" clears due time on Today and marks all-day on another date.
        var noTimeToday = AgendaLogic.ParseTaskEdit("Fixed title", now, 0, now.Date, earlierToday, originalAllDay: false);
        Assert.Equal(("Fixed title", (DateTime?)null, false), (noTimeToday.Title, noTimeToday.DueTime, noTimeToday.AllDay));

        var noTimeOther = AgendaLogic.ParseTaskEdit("Fixed title", now, 0, dec5, earlierToday, originalAllDay: false);
        Assert.Equal(("Fixed title", dec5, true), (noTimeOther.Title, noTimeOther.DueTime, noTimeOther.AllDay));
    }

    [Fact]
    public void ComputeSnoozeTargetComputesRelativeTomorrowNextWeekAndClear()
    {
        var now = new DateTime(2026, 11, 27, 15, 10, 0);
        var timedDue = new DateTime(2026, 11, 27, 14, 0, 0);
        var allDayDue = new DateTime(2026, 11, 27);

        Assert.Equal((now.AddMinutes(10), false), AgendaLogic.ComputeSnoozeTarget(allDayDue, true, TaskSnoozePreset.Minutes10, now));
        Assert.Equal((now.AddMinutes(30), false), AgendaLogic.ComputeSnoozeTarget(null, false, TaskSnoozePreset.Minutes30, now));
        Assert.Equal((now.AddHours(1), false), AgendaLogic.ComputeSnoozeTarget(timedDue, false, TaskSnoozePreset.Hour1, now));
        Assert.Equal((now.AddHours(3), false), AgendaLogic.ComputeSnoozeTarget(timedDue, false, TaskSnoozePreset.Hours3, now));

        // Tomorrow keeps clock time for timed tasks, and sets all-day for untimed/all-day tasks.
        Assert.Equal((now.Date.AddDays(1).AddHours(14), false), AgendaLogic.ComputeSnoozeTarget(timedDue, false, TaskSnoozePreset.Tomorrow, now));
        Assert.Equal((now.Date.AddDays(1), true), AgendaLogic.ComputeSnoozeTarget(allDayDue, true, TaskSnoozePreset.Tomorrow, now));
        Assert.Equal((now.Date.AddDays(1), true), AgendaLogic.ComputeSnoozeTarget(null, false, TaskSnoozePreset.Tomorrow, now));

        // Next week anchors on today if due <= today, or on the future date if already in the future.
        Assert.Equal((now.Date.AddDays(7).AddHours(14), false), AgendaLogic.ComputeSnoozeTarget(timedDue, false, TaskSnoozePreset.NextWeek, now));
        var futureDue = new DateTime(2026, 12, 5, 9, 30, 0);
        Assert.Equal((futureDue.AddDays(7), false), AgendaLogic.ComputeSnoozeTarget(futureDue, false, TaskSnoozePreset.NextWeek, now));
        Assert.Equal((futureDue.Date.AddDays(7), true), AgendaLogic.ComputeSnoozeTarget(futureDue.Date, true, TaskSnoozePreset.NextWeek, now));

        // ClearDue removes due time and clears all-day.
        Assert.Equal(((DateTime?)null, false), AgendaLogic.ComputeSnoozeTarget(allDayDue, true, TaskSnoozePreset.ClearDue, now));
    }
}
