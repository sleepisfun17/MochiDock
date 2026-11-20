using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MochiDock.Core;

/// <summary>Seconds played per game per local day, kept for the last <see cref="KeepDays"/> days.</summary>
public sealed class GamePlaytimeLedger
{
    public const int KeepDays = 90;
    public const int MaxGameNameLength = 60;

    /// <summary>Game name -> ("yyyy-MM-dd" -> seconds). Plain dictionaries so the ledger serialises as readable JSON.</summary>
    public Dictionary<string, Dictionary<string, double>> Games
    {
        get => _games;
        // A deserialiser hands over a case-sensitive dictionary; keep names case-insensitive and merge names that differ only by case.
        set
        {
            var merged = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (game, days) in value ?? new())
            {
                if (!merged.TryGetValue(game, out var target)) merged[game] = target = new Dictionary<string, double>();
                foreach (var (day, seconds) in days ?? new()) target[day] = target.GetValueOrDefault(day) + seconds;
            }
            _games = merged;
        }
    }
    private Dictionary<string, Dictionary<string, double>> _games = new(StringComparer.OrdinalIgnoreCase);

    public static string NormalizeName(string? name)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return "Game";
        return trimmed.Length > MaxGameNameLength ? trimmed[..(MaxGameNameLength - 1)].TrimEnd() + "…" : trimmed;
    }

    private static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Adds the time from <paramref name="start"/> to <paramref name="end"/> (local times) to a game, split at midnight.</summary>
    public void Add(string? game, DateTime start, DateTime end)
    {
        if (end <= start) return;
        string name = NormalizeName(game);
        if (!Games.TryGetValue(name, out var days)) Games[name] = days = new Dictionary<string, double>();
        for (var cursor = start; cursor < end;)
        {
            var midnight = cursor.Date.AddDays(1);
            var sliceEnd = end < midnight ? end : midnight;
            string key = Key(cursor);
            days[key] = days.GetValueOrDefault(key) + (sliceEnd - cursor).TotalSeconds;
            cursor = sliceEnd;
        }
    }

    /// <summary>Drops days older than <see cref="KeepDays"/> and games left with no days.</summary>
    public void Prune(DateTime now)
    {
        string oldest = Key(now.Date.AddDays(-(KeepDays - 1)));
        foreach (var game in Games.Keys.ToList())
        {
            var days = Games[game];
            foreach (var key in days.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList()) days.Remove(key);
            if (days.Count == 0) Games.Remove(game);
        }
    }

    /// <summary>Seconds played per game from <paramref name="from"/> to <paramref name="to"/> (both days included), most played first.</summary>
    public List<(string Game, double Seconds)> Between(DateTime from, DateTime to)
    {
        string a = Key(from), b = Key(to);
        return Games
            .Select(g => (Game: g.Key, Seconds: g.Value.Where(d => string.CompareOrdinal(d.Key, a) >= 0 && string.CompareOrdinal(d.Key, b) <= 0).Sum(d => d.Value)))
            .Where(x => x.Seconds > 0)
            .OrderByDescending(x => x.Seconds)
            .ToList();
    }

    public double TotalBetween(DateTime from, DateTime to) => Between(from, to).Sum(x => x.Seconds);

    public double Today(DateTime now) => TotalBetween(now, now);

    /// <summary>The first day of the week that contains <paramref name="now"/>: Monday or Sunday.</summary>
    public static DateTime WeekStart(DateTime now, bool mondayFirst)
    {
        int offset = mondayFirst ? ((int)now.DayOfWeek + 6) % 7 : (int)now.DayOfWeek;
        return now.Date.AddDays(-offset);
    }

    public double ThisWeek(DateTime now, bool mondayFirst) => TotalBetween(WeekStart(now, mondayFirst), now);
}

/// <summary>When to nudge the player to take a break.</summary>
public static class BreakReminder
{
    /// <summary>The intervals offered in settings, in minutes; 0 switches the reminder off.</summary>
    public static readonly int[] Choices = { 0, 60, 90, 120 };

    public static int NormalizeInterval(int minutes) => Choices.Contains(minutes) ? minutes : 0;

    /// <summary>
    /// True when a reminder is due: the reminder is on, the game has been played for at least a minute, and a full interval has
    /// passed since the later of the session start and the last reminder, so it repeats once per interval and never right at the start.
    /// </summary>
    public static bool Due(DateTime now, DateTime sessionStart, DateTime? lastReminder, int intervalMinutes)
    {
        if (NormalizeInterval(intervalMinutes) == 0) return false;
        if (now - sessionStart < TimeSpan.FromMinutes(1)) return false;
        var since = lastReminder is { } last && last > sessionStart ? last : sessionStart;
        return now - since >= TimeSpan.FromMinutes(intervalMinutes);
    }
}

/// <summary>Text for playtime totals.</summary>
public static class PlaytimeFormat
{
    /// <summary>"1h 05m" from an hour on, "42m" below, "0m" for nothing.</summary>
    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{(int)t.TotalMinutes}m";
    }
}
