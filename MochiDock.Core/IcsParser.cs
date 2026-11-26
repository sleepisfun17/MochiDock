using System.Globalization;
using System.Text;

namespace MochiDock.Core;

/// <summary>One calendar entry on the Today agenda. Start/End are in the display time zone (all-day: End is exclusive, midnight-aligned).</summary>
public sealed record AgendaEvent(string Uid, string Title, DateTime Start, DateTime End, bool AllDay, string Place, string? JoinUrl, string CalendarName = "", string CalendarColor = "");

/// <summary>A date or date-time as written in the feed, resolved later against a display time zone.</summary>
public readonly record struct IcsTime(DateTime Wall, bool IsUtc, string? Tzid, bool DateOnly)
{
    /// <summary>Wall time in the display zone. All-day dates and floating times are not shifted.</summary>
    public DateTime ToDisplay(TimeZoneInfo display)
    {
        if (DateOnly) return Wall.Date;
        if (IsUtc) return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(Wall, DateTimeKind.Utc), display);
        if (Tzid is null) return Wall;
        var zone = IcsParser.ResolveZone(Tzid);
        if (zone is null) return Wall;
        var wall = DateTime.SpecifyKind(Wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) wall = wall.AddHours(1);   // the hour skipped by a spring-forward change
        return TimeZoneInfo.ConvertTime(wall, zone, display);
    }
}

public sealed class IcsRawEvent
{
    public string Uid = "";
    public string Summary = "", Description = "", Location = "", Url = "", Conference = "";
    public IcsTime? Start, End, RecurrenceId;
    public TimeSpan? Duration;
    public string? RRule;
    public readonly List<IcsTime> ExDates = new();
    public bool Cancelled;
}

public sealed class IcsCalendar
{
    public readonly List<IcsRawEvent> Events = new();
    public readonly List<string> Warnings = new();
}

/// <summary>
/// Reads the iCalendar feed Google publishes as a "secret address" (and similar feeds): VEVENTs with all-day dates, TZID/UTC times,
/// RRULE recurrence (DAILY/WEEKLY/MONTHLY/YEARLY with INTERVAL, COUNT, UNTIL, BYDAY, BYMONTHDAY, BYMONTH), EXDATE, RECURRENCE-ID overrides and cancellations.
/// Anything it cannot expand (RDATE, BYSETPOS, sub-daily frequencies, ...) degrades to the single first occurrence and is reported in Warnings; it never throws on bad input.
/// </summary>
public static class IcsParser
{
    const int MaxIterations = 4000;

    public static bool LooksLikeCalendar(string? text) => text is not null && text.TrimStart('﻿', ' ', '\r', '\n').StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<AgendaEvent> Read(string text, DateTime from, DateTime to, TimeZoneInfo? display = null) =>
        Expand(Parse(text), from, to, display ?? TimeZoneInfo.Local);

    // ---- parsing ----------------------------------------------------------------------------------------------------

    public static IcsCalendar Parse(string text)
    {
        var calendar = new IcsCalendar();
        IcsRawEvent? current = null;
        int alarmDepth = 0, zoneDepth = 0;

        foreach (var line in Unfold(text))
        {
            if (line.Length == 0) continue;
            var (name, parameters, value) = SplitProperty(line);
            switch (name)
            {
                case "BEGIN":
                    if (value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase)) current = new IcsRawEvent();
                    else if (value.Equals("VALARM", StringComparison.OrdinalIgnoreCase)) alarmDepth++;
                    else if (value.Equals("VTIMEZONE", StringComparison.OrdinalIgnoreCase)) zoneDepth++;
                    continue;
                case "END":
                    if (value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase)) { if (current is { Start: not null }) calendar.Events.Add(current); current = null; }
                    else if (value.Equals("VALARM", StringComparison.OrdinalIgnoreCase)) alarmDepth = Math.Max(0, alarmDepth - 1);
                    else if (value.Equals("VTIMEZONE", StringComparison.OrdinalIgnoreCase)) zoneDepth = Math.Max(0, zoneDepth - 1);
                    continue;
            }
            if (current is null || alarmDepth > 0 || zoneDepth > 0) continue;

            switch (name)
            {
                case "UID": current.Uid = value.Trim(); break;
                case "SUMMARY": current.Summary = Unescape(value); break;
                case "DESCRIPTION": current.Description = Unescape(value); break;
                case "LOCATION": current.Location = Unescape(value); break;
                case "URL": current.Url = value.Trim(); break;
                case "X-GOOGLE-CONFERENCE": current.Conference = value.Trim(); break;
                case "STATUS": current.Cancelled = value.Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase); break;
                case "DTSTART": current.Start = ParseTime(value, parameters); break;
                case "DTEND": current.End = ParseTime(value, parameters); break;
                case "DURATION": current.Duration = ParseDuration(value); break;
                case "RRULE": current.RRule = value.Trim(); break;
                case "RECURRENCE-ID": current.RecurrenceId = ParseTime(value, parameters); break;
                case "EXDATE":
                    foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        if (ParseTime(part, parameters) is { } ex) current.ExDates.Add(ex);
                    break;
                case "RDATE": calendar.Warnings.Add($"RDATE ignored for '{current.Summary}'"); break;
            }
        }
        return calendar;
    }

    static IEnumerable<string> Unfold(string text)
    {
        var sb = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t')) { sb.Append(raw, 1, raw.Length - 1); continue; }
            if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
            sb.Append(raw.TrimStart('﻿'));
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    static (string Name, Dictionary<string, string> Params, string Value) SplitProperty(string line)
    {
        // NAME;PARAM=a;PARAM2="b:c":VALUE  (a colon inside quotes does not end the name part)
        int colon = -1; bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted) { colon = i; break; }
        }
        if (colon < 0) return (line.Trim().ToUpperInvariant(), new(), "");
        var head = line[..colon];
        var parts = new List<string>(); int start = 0; quoted = false;
        for (int i = 0; i < head.Length; i++)
        {
            if (head[i] == '"') quoted = !quoted;
            else if (head[i] == ';' && !quoted) { parts.Add(head[start..i]); start = i + 1; }
        }
        parts.Add(head[start..]);
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts.Skip(1))
        {
            int eq = p.IndexOf('=');
            if (eq > 0) parameters[p[..eq].Trim()] = p[(eq + 1)..].Trim().Trim('"');
        }
        return (parts[0].Trim().ToUpperInvariant(), parameters, line[(colon + 1)..]);
    }

    static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0) return value;
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                char c = value[++i];
                sb.Append(c is 'n' or 'N' ? '\n' : c);
            }
            else sb.Append(value[i]);
        }
        return sb.ToString();
    }

    static IcsTime? ParseTime(string value, Dictionary<string, string> parameters)
    {
        value = value.Trim();
        bool dateOnly = parameters.TryGetValue("VALUE", out var kind) && kind.Equals("DATE", StringComparison.OrdinalIgnoreCase) || value.Length == 8;
        if (dateOnly)
            return DateTime.TryParseExact(value[..Math.Min(8, value.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? new IcsTime(d, false, null, true) : null;
        bool utc = value.EndsWith('Z');
        var trimmed = value.TrimEnd('Z');
        foreach (var format in new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" })
            if (DateTime.TryParseExact(trimmed, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return new IcsTime(dt, utc, utc ? null : parameters.GetValueOrDefault("TZID"), false);
        return null;
    }

    static TimeSpan? ParseDuration(string value)
    {
        // RFC 5545 DURATION: [+-]P[nW][nD][T[nH][nM][nS]]
        var v = value.Trim(); if (v.Length < 2) return null;
        bool negative = v[0] == '-'; v = v.TrimStart('+', '-');
        if (v.Length == 0 || v[0] != 'P') return null;
        double total = 0; bool inTime = false; var number = new StringBuilder();
        foreach (var c in v[1..])
        {
            if (c == 'T') { inTime = true; continue; }
            if (char.IsDigit(c)) { number.Append(c); continue; }
            if (number.Length == 0 || !double.TryParse(number.ToString(), CultureInfo.InvariantCulture, out var n)) return null;
            number.Clear();
            total += c switch { 'W' => n * 7 * 86400, 'D' => n * 86400, 'H' => n * 3600, 'M' => inTime ? n * 60 : double.NaN, 'S' => n, _ => double.NaN };
        }
        return double.IsNaN(total) ? null : TimeSpan.FromSeconds(negative ? -total : total);
    }

    internal static TimeZoneInfo? ResolveZone(string tzid)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(tzid); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { }
        try { if (TimeZoneInfo.TryConvertIanaIdToWindowsId(tzid, out var win) && win is not null) return TimeZoneInfo.FindSystemTimeZoneById(win); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { }
        return null;
    }

    // ---- expansion --------------------------------------------------------------------------------------------------

    /// <summary>Events overlapping [from, to) in the display zone, ordered by start.</summary>
    public static IReadOnlyList<AgendaEvent> Expand(IcsCalendar calendar, DateTime from, DateTime to, TimeZoneInfo display)
    {
        var result = new List<AgendaEvent>();
        var overrides = calendar.Events.Where(e => e.RecurrenceId is not null).GroupBy(e => e.Uid).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var ev in calendar.Events)
        {
            if (ev.Start is not { } start) continue;
            if (ev.RecurrenceId is not null) { AddIfOverlapping(result, ev, start.ToDisplay(display), display, from, to, ev.Cancelled); continue; }

            var duration = Duration(ev, start, display);
            var excluded = new HashSet<DateTime>(ev.ExDates.Select(x => x.ToDisplay(display)));
            if (overrides.TryGetValue(ev.Uid, out var replaced))
                foreach (var o in replaced) if (o.RecurrenceId is { } rid) excluded.Add(rid.ToDisplay(display));

            if (string.IsNullOrWhiteSpace(ev.RRule))
            {
                var s = start.ToDisplay(display);
                if (!excluded.Contains(s)) AddIfOverlapping(result, ev, s, display, from, to, ev.Cancelled, duration);
                continue;
            }

            foreach (var occurrence in Occurrences(ev.RRule!, start, display, from, to, duration, calendar.Warnings, ev.Summary))
                if (!excluded.Contains(occurrence)) AddIfOverlapping(result, ev, occurrence, display, from, to, ev.Cancelled, duration);
        }
        return result.OrderBy(e => e.Start).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static TimeSpan Duration(IcsRawEvent ev, IcsTime start, TimeZoneInfo display)
    {
        var s = start.ToDisplay(display);
        if (ev.End is { } end) { var d = end.ToDisplay(display) - s; if (d >= TimeSpan.Zero) return start.DateOnly && d == TimeSpan.Zero ? TimeSpan.FromDays(1) : d; }
        if (ev.Duration is { } dur && dur >= TimeSpan.Zero) return dur;
        return start.DateOnly ? TimeSpan.FromDays(1) : TimeSpan.Zero;
    }

    static void AddIfOverlapping(List<AgendaEvent> result, IcsRawEvent ev, DateTime start, TimeZoneInfo display, DateTime from, DateTime to, bool cancelled, TimeSpan? duration = null)
    {
        if (cancelled) return;
        var dur = duration ?? Duration(ev, ev.Start!.Value, display);
        var end = start + dur;
        bool overlaps = dur == TimeSpan.Zero ? start >= from && start < to : start < to && end > from;
        if (!overlaps) return;
        bool allDay = ev.Start!.Value.DateOnly;
        result.Add(new AgendaEvent(ev.Uid, string.IsNullOrWhiteSpace(ev.Summary) ? "(No title)" : ev.Summary.Trim(), start, end, allDay,
            ev.Location.Trim(), AgendaLogic.FindJoinUrl(ev.Conference, ev.Url, ev.Location, ev.Description)));
    }

    // ---- RRULE ------------------------------------------------------------------------------------------------------

    sealed class Rule
    {
        public string Freq = "";
        public int Interval = 1, Count;
        public IcsTime? Until;
        public DayOfWeek WeekStart = DayOfWeek.Monday;
        public List<(int? Ordinal, DayOfWeek Day)> ByDay = new();
        public List<int> ByMonthDay = new(), ByMonth = new();
        public bool Unsupported;
    }

    static Rule? ParseRule(string text)
    {
        var rule = new Rule();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('='); if (eq < 0) continue;
            string key = part[..eq].Trim().ToUpperInvariant(), value = part[(eq + 1)..].Trim();
            switch (key)
            {
                case "FREQ": rule.Freq = value.ToUpperInvariant(); break;
                case "INTERVAL": rule.Interval = int.TryParse(value, out var i) && i > 0 ? i : 1; break;
                case "COUNT": rule.Count = int.TryParse(value, out var c) && c > 0 ? c : 0; break;
                case "UNTIL": rule.Until = ParseTime(value, new()); break;
                case "WKST": rule.WeekStart = ParseDay(value) ?? DayOfWeek.Monday; break;
                case "BYMONTHDAY": rule.ByMonthDay = value.Split(',').Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n != 0).ToList(); break;
                case "BYMONTH": rule.ByMonth = value.Split(',').Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n is >= 1 and <= 12).ToList(); break;
                case "BYDAY":
                    foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var t = token.Trim().ToUpperInvariant();
                        if (t.Length < 2 || ParseDay(t[^2..]) is not { } day) { rule.Unsupported = true; continue; }
                        int? ordinal = t.Length > 2 && int.TryParse(t[..^2], out var o) ? o : null;
                        rule.ByDay.Add((ordinal, day));
                    }
                    break;
                case "BYSETPOS": case "BYWEEKNO": case "BYYEARDAY": case "BYHOUR": case "BYMINUTE": case "BYSECOND": rule.Unsupported = true; break;
            }
        }
        return rule.Freq is "DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY" ? rule : null;
    }

    static DayOfWeek? ParseDay(string code) => code switch { "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday, "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday, _ => null };

    static IEnumerable<DateTime> Occurrences(string ruleText, IcsTime start, TimeZoneInfo display, DateTime from, DateTime to, TimeSpan duration, List<string> warnings, string title)
    {
        var rule = ParseRule(ruleText);
        var first = start.ToDisplay(display);
        if (rule is null || rule.Unsupported)
        {
            warnings.Add($"Recurrence '{ruleText}' not fully supported for '{title}'");
            if (rule is null) { yield return first; yield break; }
        }

        // Occurrence dates are generated on the series' own wall clock (so a TZID series keeps its wall time across DST), then converted to the display zone.
        DateTime seriesStartDate = start.Wall.Date;
        TimeSpan timeOfDay = start.DateOnly ? TimeSpan.Zero : start.Wall.TimeOfDay;
        var sourceZone = start.Tzid is not null && !start.IsUtc ? ResolveZone(start.Tzid) : null;
        DateTime? untilLocal = rule.Until?.ToDisplay(display);
        bool untilIsDate = rule.Until?.DateOnly == true;

        DateTime Resolve(DateTime date)
        {
            if (start.DateOnly) return date.Date;
            var wall = date.Date + timeOfDay;
            if (start.IsUtc) return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(wall, DateTimeKind.Utc), display);
            if (sourceZone is null) return wall;   // floating time, or a zone this machine does not know: shown as written
            if (sourceZone.IsInvalidTime(wall)) wall = wall.AddHours(1);
            return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(wall, DateTimeKind.Unspecified), sourceZone, display);
        }

        DateTime windowEnd = to + duration + TimeSpan.FromDays(2);
        int emitted = 0, iterations = 0;

        foreach (var date in CandidateDates(rule, seriesStartDate))
        {
            if (++iterations > MaxIterations) yield break;
            var local = Resolve(date);
            if (untilLocal is { } u && (untilIsDate ? local.Date > u.Date : local > u)) yield break;
            if (local > windowEnd) yield break;
            emitted++;
            if (rule.Count > 0 && emitted > rule.Count) yield break;
            yield return local;
        }
    }

    static IEnumerable<DateTime> CandidateDates(Rule rule, DateTime startDate)
    {
        startDate = startDate.Date;
        switch (rule.Freq)
        {
            case "DAILY":
                for (int k = 0; ; k++) yield return startDate.AddDays((double)k * rule.Interval);
            case "WEEKLY":
            {
                var days = rule.ByDay.Count > 0 ? rule.ByDay.Select(d => d.Day).Distinct().ToList() : new List<DayOfWeek> { startDate.DayOfWeek };
                var offsets = days.Select(d => ((int)d - (int)rule.WeekStart + 7) % 7).OrderBy(o => o).ToList();
                var weekStart = startDate.AddDays(-(((int)startDate.DayOfWeek - (int)rule.WeekStart + 7) % 7));
                for (int w = 0; ; w++)
                    foreach (var o in offsets)
                    {
                        var date = weekStart.AddDays(w * 7.0 * rule.Interval + o);
                        if (date >= startDate) yield return date;
                    }
            }
            case "MONTHLY":
                for (int m = 0; ; m++)
                {
                    var month = new DateTime(startDate.Year, startDate.Month, 1).AddMonths(m * rule.Interval);
                    foreach (var date in DatesInMonth(rule, month, startDate)) if (date >= startDate) yield return date;
                }
            case "YEARLY":
                for (int y = 0; ; y++)
                {
                    int year = startDate.Year + y * rule.Interval;
                    var months = rule.ByMonth.Count > 0 ? rule.ByMonth.OrderBy(x => x).ToList() : new List<int> { startDate.Month };
                    foreach (var mo in months)
                        foreach (var date in DatesInMonth(rule, new DateTime(year, mo, 1), startDate)) if (date >= startDate) yield return date;
                }
        }
    }

    static IEnumerable<DateTime> DatesInMonth(Rule rule, DateTime month, DateTime startDate)
    {
        int days = DateTime.DaysInMonth(month.Year, month.Month);
        var set = new SortedSet<DateTime>();
        if (rule.ByMonthDay.Count > 0)
        {
            foreach (var d in rule.ByMonthDay) { int day = d > 0 ? d : days + d + 1; if (day >= 1 && day <= days) set.Add(new DateTime(month.Year, month.Month, day)); }
        }
        else if (rule.ByDay.Count > 0)
        {
            foreach (var (ordinal, dow) in rule.ByDay)
            {
                var matching = Enumerable.Range(1, days).Select(d => new DateTime(month.Year, month.Month, d)).Where(d => d.DayOfWeek == dow).ToList();
                if (ordinal is null) { foreach (var d in matching) set.Add(d); }
                else
                {
                    int index = ordinal > 0 ? ordinal.Value - 1 : matching.Count + ordinal.Value;
                    if (index >= 0 && index < matching.Count) set.Add(matching[index]);
                }
            }
        }
        else if (startDate.Day <= days) set.Add(new DateTime(month.Year, month.Month, startDate.Day));
        return set;
    }
}
