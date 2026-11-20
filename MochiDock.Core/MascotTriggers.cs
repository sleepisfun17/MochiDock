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

    /// <summary>No keyboard or mouse input for this long (with nothing playing) makes Mochi lonely.</summary>
    public static readonly TimeSpan LonelyAfter = TimeSpan.FromMinutes(20);

    /// <summary>Away from the PC for at least this long, he wakes up yawning when the user comes back.</summary>
    public static readonly TimeSpan WakingAfterAway = TimeSpan.FromHours(4);

    /// <summary>
    /// Lonely: the user has been away from the keyboard and mouse for a long time while the screen is on and nothing is going on
    /// (no media, no game, not locked). The idle time must be a real reading: zero means the system could not say.
    /// </summary>
    public static bool Lonely(TimeSpan idle, bool mediaPlaying, bool gaming, bool locked, bool displayAwake) =>
        idle >= LonelyAfter && !mediaPlaying && !gaming && !locked && displayAwake;

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

    /// <summary>True when an open task with a due time is overdue by 10 to 90 minutes and nobody acknowledged it.</summary>
    public static bool UnacknowledgedOverdue(IEnumerable<ReminderTask> tasks, DateTime now, ISet<string> acknowledged) =>
        tasks.Any(t => !t.Done && t.Due is { } due && now - due >= AngryFrom && now - due < AngryUntil && !acknowledged.Contains(AgendaReminderPolicy.TaskItemKey(t.Id, due)));
}
