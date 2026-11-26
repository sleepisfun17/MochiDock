using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>The yes/no inputs behind Mochi's ambient faces. Pure, so each rule is tested; the view model only feeds them readings.</summary>
public static class MascotTriggers
{
    public const int BatteryLowPercent = 15, BatteryRecoverPercent = 20;
    public const double GpuHotClearMargin = 3;
    /// <summary>An unacknowledged task makes Mochi angry from 10 minutes past its due time until 90 minutes past it (30 minutes after the last nudge).</summary>
    public static readonly TimeSpan AngryFrom = TimeSpan.FromMinutes(10), AngryUntil = TimeSpan.FromMinutes(90);

    /// <summary>Low and unplugged: at or below 15%, until the charger is connected or the level is back at 20%. No battery means never.</summary>
    public static bool BatteryLow(PowerState state, bool currentlyLow)
    {
        if (!state.HasBattery || state.Percent is < 0 or > 100 || state.OnAc) return false;
        if (state.Percent <= BatteryLowPercent) return true;
        return currentlyLow && state.Percent < BatteryRecoverPercent;
    }

    /// <summary>
    /// The GPU is at or above its alert temperature. Only a measured, fresh reading (at most 5 seconds old) counts, like the hardware
    /// alerts; the face clears 3 degrees below the threshold. A missing or stale reading never keeps Mochi hot.
    /// </summary>
    public static bool GpuHot(AlertReading? reading, double threshold, bool currentlyHot, DateTimeOffset now)
    {
        if (reading is null || !reading.Measured || !double.IsFinite(reading.Value) || reading.CapturedAt > now || now - reading.CapturedAt > TimeSpan.FromSeconds(5)) return false;
        if (reading.Value >= threshold) return true;
        return currentlyHot && reading.Value >= threshold - GpuHotClearMargin;
    }

    /// <summary>A measured CPU load for the dizzy face: the value when it is measured and fresh, else NaN (which breaks the run).</summary>
    public static double MeasuredLoad(AlertReading? reading, DateTimeOffset now) =>
        reading is { Measured: true } r && double.IsFinite(r.Value) && r.CapturedAt <= now && now - r.CapturedAt <= TimeSpan.FromSeconds(5) ? r.Value : double.NaN;


    /// <summary>Away from the PC for at least this long, he wakes up yawning when the user comes back.</summary>
    public static readonly TimeSpan WakingAfterAway = TimeSpan.FromHours(4);


    /// <summary>Back after a long absence: a few yawning seconds.</summary>
    public static bool WakingUp(TimeSpan away) => away >= WakingAfterAway;

    /// <summary>The app starts in the morning (05:00 to 09:59): the first seconds are sleepy.</summary>
    public static bool WakingOnStart(DateTime now) => now.Hour is >= 5 and < 10;

    /// <summary>The placement from which a battle royale result counts as a defeat (outside the top ten).</summary>
    public const int DefeatPlacementAbove = 10;

    /// <summary>
    /// Victory for a won match, Defeat for a lost one, null when there is nothing to celebrate or mourn. League of Legends reports Win or
    /// Loss; a battle royale match is a victory for first place and a defeat outside the top ten (top ten stays neutral).
    /// </summary>
    public static MascotMood? MatchMood(GameMatchSummary? match)
    {
        if (match == null) return null;
        if (match.Result == "Win" || match.Placement == 1) return MascotMood.Victory;
        if (match.Result == "Loss" || match.Placement > DefeatPlacementAbove) return MascotMood.Defeat;
        return null;
    }

    /// <summary>The FX rate fell by at least this much (percent over 24 hours) for the sad money face.</summary>
    public const decimal MoneyDownBelowPercent = -0.1m;

    /// <summary>Where the user is in the dock, for <see cref="ActivityFace"/>.</summary>
    public enum DockView { Other, StatsBattery, Fx, Weather }

    /// <summary>
    /// The activity face, or null for none. A running Pomodoro wins over the open card: Studying while focusing, the coffee face
    /// (BreakTime) during its break. Otherwise the open card picks it: the Stats Battery page is Electric, FX shows money (sad when the
    /// rate is down), Weather shows the current sky. The dock must be expanded for a card face.
    /// </summary>
    public static MascotMood? ActivityFace(bool pomodoroWorking, bool expanded, DockView view, decimal? fxChangePercent, string? weatherIconKind, DateTime now, bool pomodoroBreak = false)
    {
        if (pomodoroWorking) return MascotMood.Studying;
        if (pomodoroBreak) return MascotMood.BreakTime;
        if (!expanded) return null;
        return view switch
        {
            DockView.StatsBattery => MascotMood.Electric,
            DockView.Fx => MoneyFace(fxChangePercent),
            DockView.Weather => WeatherFace(weatherIconKind, now),
            _ => null,
        };
    }

    /// <summary>Money for a rising or flat rate (or no data), MoneyDown when it fell by at least 0.1%.</summary>
    public static MascotMood MoneyFace(decimal? changePercent) =>
        changePercent is { } c && c <= MoneyDownBelowPercent ? MascotMood.MoneyDown : MascotMood.Money;

    /// <summary>
    /// The sky face for a weather icon kind: rain, drizzle and storms are Rainy, snow is Snowy, a clear or partly cloudy sky is Sunny by day
    /// (06:00 to 19:00) and Cloudy at night, cloud and fog are Cloudy. Unknown weather gives null (no face).
    /// </summary>
    public static MascotMood? WeatherFace(string? iconKind, DateTime now) => iconKind switch
    {
        "Rain" or "Drizzle" or "Thunder" => MascotMood.Rainy,
        "Snow" => MascotMood.Snowy,
        "Clear" or "PartlyCloudy" => now.Hour is >= 6 and < 19 ? MascotMood.Sunny : MascotMood.Cloudy,
        "Cloudy" or "Fog" => MascotMood.Cloudy,
        _ => null,
    };

    /// <summary>Late night for the yawning face: from 23:00 until 05:00.</summary>
    public static bool IsLateNight(DateTime now) => now.Hour is >= 23 or < 5;

    /// <summary>
    /// The face for the day, or null: Meeting during a timed calendar meeting, Birthday on the user's birthday (<paramref name="birthdayMonthDay"/>
    /// "MM-dd"), active calendar Eating/Workout/Working, Proud once every task due today is done, Yawning late at night. That order wins.
    /// </summary>
    public static MascotMood? DayFace(DateTime now, string? birthdayMonthDay, bool allTasksDone, bool inMeeting) =>
        DayFace(now, birthdayMonthDay, allTasksDone, inMeeting ? CalendarActivityKind.Meeting : CalendarActivityKind.None);

    public static MascotMood? DayFace(DateTime now, string? birthdayMonthDay, bool allTasksDone, CalendarActivityKind calendarActivity, bool onWorkBreak = false)
    {
        if (calendarActivity == CalendarActivityKind.Meeting) return MascotMood.Meeting;
        if (IsBirthday(now, birthdayMonthDay)) return MascotMood.Birthday;
        if (calendarActivity == CalendarActivityKind.Eating) return MascotMood.Eating;
        if (calendarActivity == CalendarActivityKind.Workout) return MascotMood.Workout;
        if (calendarActivity == CalendarActivityKind.Working) return onWorkBreak ? MascotMood.Coffee : MascotMood.Working;
        if (allTasksDone) return MascotMood.Proud;
        if (IsLateNight(now)) return MascotMood.Yawning;
        return null;
    }

    /// <summary>Whether today is the birthday "MM-dd" (29 February is kept on 28 February in other years). An empty or unreadable value is never.</summary>
    public static bool IsBirthday(DateTime now, string? monthDay)
    {
        if (ParseMonthDay(monthDay) is not { } md) return false;
        int day = md.Month == 2 && md.Day == 29 && !DateTime.IsLeapYear(now.Year) ? 28 : md.Day;
        return now.Month == md.Month && now.Day == day;
    }

    /// <summary>"MM-dd" as a month and day, or null when empty or not a real date (29 February is allowed).</summary>
    public static (int Month, int Day)? ParseMonthDay(string? monthDay)
    {
        var parts = (monthDay ?? "").Trim().Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int month) || !int.TryParse(parts[1], out int day)) return null;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2024, month)) return null;
        return (month, day);
    }

    /// <summary>
    /// True when at least one task is due today, every task due today is done, and no earlier task is still open (overdue).
    /// Tasks without a due time are not counted: a someday list never blocks the face.
    /// </summary>
    public static bool AllTasksDone(IEnumerable<(DateTime? Due, bool Done)> tasks, DateTime now)
    {
        bool any = false;
        foreach (var (due, done) in tasks)
        {
            if (due is not { } d) continue;
            if (d.Date == now.Date) { if (!done) return false; any = true; }
            else if (d.Date < now.Date && !done) return false;
        }
        return any;
    }

    /// <summary>The dock counts as untouched once the pointer has not been on it for this long.</summary>
    public static readonly TimeSpan DockQuietAfter = TimeSpan.FromSeconds(90);

    /// <summary>Nobody at the PC: no keyboard or mouse input for this long, and Mochi really sleeps.</summary>
    public static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(10);

    /// <summary>While you work elsewhere, Mochi changes his resting face this often.</summary>
    public static readonly TimeSpan RestingStep = TimeSpan.FromMinutes(2);

    // The resting rotation: the idle thoughts (null: leave the Idle/Waiting blink alone), a coffee, a bored sigh, a short doze.
    private static readonly MascotMood?[] RestingCycle = { null, MascotMood.Coffee, MascotMood.Bored, MascotMood.Sleeping };

    /// <summary>
    /// Mochi's resting face, or null to leave his face alone. Sleeping once the PC has had no keyboard or mouse input for
    /// <see cref="AwayAfter"/> (nobody is there). While someone is working but has not touched the dock for <see cref="DockQuietAfter"/>
    /// (typing, coding), he rotates every <see cref="RestingStep"/> through the idle thoughts, a coffee, a bored sigh and a short doze,
    /// instead of falling asleep. Nothing while music plays, a game runs, the PC is locked or the screen is off (those have their own faces).
    /// </summary>
    public static MascotMood? RestingFace(TimeSpan dockUntouched, TimeSpan inputIdle, bool mediaPlaying, bool gaming, bool locked, bool displayAwake)
    {
        if (gaming || locked || !displayAwake || mediaPlaying) return null;
        if (inputIdle >= AwayAfter) return MascotMood.Sleeping;
        if (dockUntouched < DockQuietAfter) return null;
        long step = (dockUntouched - DockQuietAfter).Ticks / RestingStep.Ticks;
        return RestingCycle[step % RestingCycle.Length];
    }

    /// <summary>True when an open task with a due time is overdue by 10 to 90 minutes and nobody acknowledged it.</summary>
    public static bool UnacknowledgedOverdue(IEnumerable<ReminderTask> tasks, DateTime now, ISet<string> acknowledged) =>
        tasks.Any(t => !t.Done && t.Due is { } due && now - due >= AngryFrom && now - due < AngryUntil && !acknowledged.Contains(AgendaReminderPolicy.TaskItemKey(t.Id, due)));
}

/// <summary>
/// Tracks continuous active work during a calendar Working schedule and triggers a 5-minute Coffee break every 45 minutes.
/// Stepping away from input for 3+ minutes (or locking / leaving the Working block) resets the work timer.
/// </summary>
public sealed class WorkBreakTracker
{
    public static readonly TimeSpan WorkBreakAfter = TimeSpan.FromMinutes(45);
    public static readonly TimeSpan WorkBreakDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan WorkBreakResetIdle = TimeSpan.FromMinutes(3);

    private DateTime? _workStartedAt;
    private DateTime? _breakStartedAt;

    public bool OnBreak => _breakStartedAt.HasValue;

    public void Reset()
    {
        _workStartedAt = null;
        _breakStartedAt = null;
    }

    public bool Update(DateTime now, bool isWorking, TimeSpan inputIdle, bool awayOrLocked)
    {
        if (!isWorking || awayOrLocked)
        {
            Reset();
            return false;
        }
        if (_breakStartedAt is { } breakStart)
        {
            if (now - breakStart >= WorkBreakDuration)
            {
                _breakStartedAt = null;
                _workStartedAt = now;
                return false;
            }
            return true;
        }
        if (inputIdle >= WorkBreakResetIdle)
        {
            _workStartedAt = null;
            return false;
        }
        _workStartedAt ??= now;
        if (now - _workStartedAt.Value >= WorkBreakAfter)
        {
            _breakStartedAt = now;
            _workStartedAt = null;
            return true;
        }
        return false;
    }
}
