using System.Text.RegularExpressions;

namespace MochiDock.Core;

public enum AgendaFocusKind { Now, Next }

/// <summary>Where a task belongs in the agenda of a selected day.</summary>
public enum AgendaTaskGroup { None, OnDay, Floating }

/// <summary>What an active timed calendar event represents for the mascot's ambient badge, ordered by priority.</summary>
public enum CalendarActivityKind { None, Working, Workout, Eating, Meeting }

/// <summary>Presets for snoozing or rescheduling a task from the Today card.</summary>
public enum TaskSnoozePreset { Minutes10, Minutes30, Hour1, Hours3, Tomorrow, NextWeek, ClearDue }

/// <summary>Parsed result of quick-add task input: cleaned title, optional due instant, and whether it is a date-only (all-day) task.</summary>
public readonly record struct ParsedTaskInput(string Title, DateTime? DueTime, bool AllDay);

/// <summary>User-defined overrides for calendar schedule badges: per event title and optional default per calendar name.</summary>
public sealed class CalendarActivityRules
{
    public IReadOnlyDictionary<string, CalendarActivityKind> ByTitle { get; }
    public IReadOnlyDictionary<string, CalendarActivityKind> ByCalendar { get; }

    public CalendarActivityRules(IReadOnlyDictionary<string, CalendarActivityKind>? byTitle = null, IReadOnlyDictionary<string, CalendarActivityKind>? byCalendar = null)
    {
        ByTitle = byTitle ?? new Dictionary<string, CalendarActivityKind>(StringComparer.OrdinalIgnoreCase);
        ByCalendar = byCalendar ?? new Dictionary<string, CalendarActivityKind>(StringComparer.OrdinalIgnoreCase);
    }

    public static CalendarActivityRules Parse(IDictionary<string, string>? byTitle, IDictionary<string, string>? byCalendar)
    {
        static Dictionary<string, CalendarActivityKind> Build(IDictionary<string, string>? source)
        {
            var map = new Dictionary<string, CalendarActivityKind>(StringComparer.OrdinalIgnoreCase);
            if (source is null) return map;
            foreach (var (k, v) in source)
            {
                var key = (k ?? "").Trim();
                if (key.Length == 0) continue;
                if (Enum.TryParse<CalendarActivityKind>(v?.Trim(), ignoreCase: true, out var kind))
                    map[key] = kind;
            }
            return map;
        }
        return new CalendarActivityRules(Build(byTitle), Build(byCalendar));
    }
}

/// <summary>The event the agenda should call out: the one happening now, or the next one starting within the hour.</summary>
public sealed record AgendaFocus(AgendaEvent Event, AgendaFocusKind Kind, string Text);

public static class AgendaLogic
{
    static readonly string[] JoinHosts = { "meet.google.com", "zoom.us", "teams.microsoft.com", "teams.live.com", "webex.com" };
    static readonly Regex UrlPattern = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex MeetingTitleAsciiPattern = new(
        @"(?<![A-Za-z0-9])(meetings?|mtg|sync|stand[\s-]?up|1:1|1-on-1|1on1|one-on-one|huddle|retro(?:spective)?|scrum|interview|webinar|town[\s-]?hall|all[\s-]?hands|kick[\s-]?off|briefing|sprint\s+(?:planning|review)|design\s+review|zoom|webex|google\s+meet|(?:ms|microsoft)\s+teams|rapat|diskusi|presentasi|wawancara|(?<!on[\s-])call)(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly string[] MeetingTitleCjkKeywords = { "会議", "ミーティング", "打ち合わせ", "打合せ", "打合わせ", "面談", "面接", "定例", "朝会", "夕会", "商談" };
    static readonly Regex MeetingPlaceAsciiPattern = new(
        @"(?<![A-Za-z0-9])(zoom|google\s+meet|(?:ms|microsoft)\s+teams|webex|huddle|meeting\s+room|conference\s+room|ruang\s+rapat)(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly string[] MeetingPlaceCjkKeywords = { "会議室" };
    static readonly Regex EatingTitleAsciiPattern = new(
        @"(?<![A-Za-z0-9])(lunch|dinner|breakfast|brunch|meal|eating|eat|supper|ramen|sushi|bento|makan(?:\s+(?:siang|malam))?|sarapan)(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly string[] EatingTitleCjkKeywords = { "ランチ", "昼食", "昼休み", "昼ご飯", "夕食", "晩ご飯", "朝食", "朝ご飯", "食事", "ご飯" };
    static readonly Regex WorkoutTitleAsciiPattern = new(
        @"(?<![A-Za-z0-9])(gym|work[\s-]?out|exercise|training|fitness|cardio|running|jogging|yoga|pilates|swim(?:ming)?|boxing|olahraga|latihan|lari)(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly string[] WorkoutTitleCjkKeywords = { "ジム", "筋トレ", "トレーニング", "運動", "ランニング", "ヨガ", "水泳" };
    static readonly Regex WorkoutPlaceAsciiPattern = new(
        @"(?<![A-Za-z0-9])(gym|fitness)(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly string[] WorkoutPlaceCjkKeywords = { "ジム" };
    static readonly Regex WorkingTitleAsciiPattern = new(
        @"(?<![A-Za-z0-9])(work|working|work\s+shift|shift|office|deep\s+work|focus\s+(?:block|time)|coding|kerja|kantor|lembur)(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly string[] WorkingTitleCjkKeywords = { "仕事", "勤務", "出社", "業務", "作業", "シフト", "出勤" };

    /// <summary>
    /// A meeting link is only ever opened if it is https, on a known conferencing host, with no user info and no custom port.
    /// Feeds are untrusted text, so anything else is ignored rather than handed to the shell.
    /// </summary>
    public static bool IsJoinUrlAllowed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return false;
        string host = uri.Host.ToLowerInvariant();
        return JoinHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
    }

    /// <summary>The first allowed meeting link among the explicit conference property, the event URL, the location and the description.</summary>
    public static string? FindJoinUrl(params string?[] sources)
    {
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            foreach (Match m in UrlPattern.Matches(source))
            {
                var candidate = m.Value.TrimEnd('.', ',', ')', ';', '>');
                if (IsJoinUrlAllowed(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Whether a task shows in the agenda of <paramref name="selectedDay"/>: <see cref="AgendaTaskGroup.OnDay"/> when it is due that day
    /// (done or not), <see cref="AgendaTaskGroup.Floating"/> for open tasks with no due time or an overdue one (today only), else none.
    /// </summary>
    public static AgendaTaskGroup TaskGroup(DateTime? due, bool done, DateTime selectedDay, DateTime today)
    {
        if (due is { } at && at.Date == selectedDay.Date) return AgendaTaskGroup.OnDay;
        if (selectedDay.Date == today.Date && !done && (due is null || due.Value.Date < today.Date)) return AgendaTaskGroup.Floating;
        return AgendaTaskGroup.None;
    }

    /// <summary>Events touching the given local day: all-day first, then by start time.</summary>
    public static List<AgendaEvent> ForDay(IEnumerable<AgendaEvent> events, DateTime day)
    {
        var start = day.Date; var end = start.AddDays(1);
        return events
            .Where(e => e.End == e.Start ? e.Start >= start && e.Start < end : e.Start < end && e.End > start)
            .OrderByDescending(e => e.AllDay).ThenBy(e => e.Start).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Days (local dates) that carry at least one event, for the calendar dots.</summary>
    public static HashSet<DateTime> DaysWithEvents(IEnumerable<AgendaEvent> events, DateTime from, DateTime to)
    {
        var days = new HashSet<DateTime>();
        foreach (var e in events)
        {
            var last = e.End > e.Start ? e.End.AddTicks(-1).Date : e.Start.Date;
            for (var d = e.Start.Date; d <= last && d < to; d = d.AddDays(1))
                if (d >= from.Date) days.Add(d);
        }
        return days;
    }

    /// <summary>Up to <paramref name="maxDotsPerDay"/> distinct normalized calendar colour keys per local date, in event order.</summary>
    public static Dictionary<DateTime, List<string>> DayEventColors(IEnumerable<AgendaEvent> events, DateTime from, DateTime to, int maxDotsPerDay = 3)
    {
        var map = new Dictionary<DateTime, List<string>>();
        if (maxDotsPerDay <= 0) return map;
        foreach (var e in events)
        {
            string color = CalendarFeedPolicy.NormalizeColor(e.CalendarColor);
            var last = e.End > e.Start ? e.End.AddTicks(-1).Date : e.Start.Date;
            for (var d = e.Start.Date; d <= last && d < to; d = d.AddDays(1))
            {
                if (d < from.Date) continue;
                if (!map.TryGetValue(d, out var list))
                    map[d] = list = new List<string>(maxDotsPerDay);
                if (list.Count < maxDotsPerDay && !list.Contains(color, StringComparer.OrdinalIgnoreCase))
                    list.Add(color);
            }
        }
        return map;
    }

    /// <summary>The event on now (the latest to start), else the next timed event starting within 60 minutes. All-day events are never the focus.</summary>
    public static AgendaFocus? Focus(IEnumerable<AgendaEvent> events, DateTime now)
    {
        var timed = events.Where(e => !e.AllDay).ToList();
        var current = timed.Where(e => e.Start <= now && e.End > now).OrderByDescending(e => e.Start).FirstOrDefault();
        if (current is not null) return new AgendaFocus(current, AgendaFocusKind.Now, $"{Minutes((current.End - now).TotalMinutes)} left");
        var next = timed.Where(e => e.Start > now && e.Start - now <= TimeSpan.FromMinutes(60)).OrderBy(e => e.Start).FirstOrDefault();
        return next is null ? null : new AgendaFocus(next, AgendaFocusKind.Next, $"in {Minutes((next.Start - now).TotalMinutes)}");
    }

    /// <summary>
    /// True when a timed calendar event looks like an actual call/meeting (has a conferencing link, or a meeting keyword in its title/place).
    /// Non-meeting schedule blocks like Gym, Lunch, or Focus time return false so the mascot stays in its normal mood.
    /// </summary>
    public static bool IsMeeting(AgendaEvent ev)
    {
        if (ev.AllDay) return false;
        if (!string.IsNullOrWhiteSpace(ev.JoinUrl)) return true;
        return MatchesActivity(ev.Title, ev.Place, MeetingTitleAsciiPattern, MeetingTitleCjkKeywords, MeetingPlaceAsciiPattern, MeetingPlaceCjkKeywords);
    }

    /// <summary>True when at least one currently active timed event (<see cref="AgendaEvent.Start"/> &lt;= <paramref name="now"/> &lt; <see cref="AgendaEvent.End"/>) is a meeting.</summary>
    public static bool HasActiveMeeting(IEnumerable<AgendaEvent> events, DateTime now) =>
        events.Any(e => !e.AllDay && e.Start <= now && e.End > now && IsMeeting(e));

    /// <summary>Classifies a single timed event into Meeting, Eating, Workout, Working, or None, applying optional title and calendar overrides.</summary>
    public static CalendarActivityKind ClassifyActivity(AgendaEvent ev, CalendarActivityRules? rules = null)
    {
        if (ev.AllDay) return CalendarActivityKind.None;

        string titleKey = (ev.Title ?? "").Trim();
        if (rules is not null && titleKey.Length > 0 && rules.ByTitle.TryGetValue(titleKey, out var titleKind))
            return titleKind;

        string calKey = (ev.CalendarName ?? "").Trim();
        CalendarActivityKind calKind = CalendarActivityKind.None;
        bool hasCalRule = rules is not null && calKey.Length > 0 && rules.ByCalendar.TryGetValue(calKey, out calKind);
        if (hasCalRule && calKind == CalendarActivityKind.None)
            return CalendarActivityKind.None;

        var auto = ClassifyActivityAuto(ev);
        if (auto != CalendarActivityKind.None) return auto;

        return hasCalRule ? calKind : CalendarActivityKind.None;
    }

    static CalendarActivityKind ClassifyActivityAuto(AgendaEvent ev)
    {
        if (IsMeeting(ev)) return CalendarActivityKind.Meeting;
        if (MatchesActivity(ev.Title, null, EatingTitleAsciiPattern, EatingTitleCjkKeywords)) return CalendarActivityKind.Eating;
        if (MatchesActivity(ev.Title, ev.Place, WorkoutTitleAsciiPattern, WorkoutTitleCjkKeywords, WorkoutPlaceAsciiPattern, WorkoutPlaceCjkKeywords)) return CalendarActivityKind.Workout;
        if (MatchesActivity(ev.Title, null, WorkingTitleAsciiPattern, WorkingTitleCjkKeywords)) return CalendarActivityKind.Working;
        return CalendarActivityKind.None;
    }

    /// <summary>Returns the highest-priority activity across all currently active timed events (Meeting &gt; Eating &gt; Workout &gt; Working).</summary>
    public static CalendarActivityKind ActiveCalendarActivity(IEnumerable<AgendaEvent> events, DateTime now, CalendarActivityRules? rules = null)
    {
        var best = CalendarActivityKind.None;
        foreach (var ev in events)
        {
            if (ev.AllDay || ev.Start > now || ev.End <= now) continue;
            var kind = ClassifyActivity(ev, rules);
            if (kind > best) best = kind;
            if (best == CalendarActivityKind.Meeting) break;
        }
        return best;
    }

    static bool MatchesActivity(string? title, string? place, Regex titleAscii, string[] titleCjk, Regex? placeAscii = null, string[]? placeCjk = null)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            if (titleAscii.IsMatch(title)) return true;
            foreach (var kw in titleCjk)
                if (title.Contains(kw, StringComparison.Ordinal)) return true;
        }
        if (!string.IsNullOrWhiteSpace(place))
        {
            if (placeAscii is not null && placeAscii.IsMatch(place)) return true;
            if (placeCjk is not null)
                foreach (var kw in placeCjk)
                    if (place.Contains(kw, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// The status of a task for the Today card: "in 17m", "in 1h 05m", "Due now" for the first minute, then "Overdue 25m",
    /// "Overdue 2h 05m" or "Overdue 3d". Empty for a done task or one without a due time.
    /// </summary>
    public static string TaskStatus(DateTime? due, bool done, DateTime now)
    {
        if (due is not { } at || done) return "";
        var diff = at - now;
        if (diff.TotalSeconds > 0)
            return diff.TotalHours >= 1 ? $"in {(int)diff.TotalHours}h {diff.Minutes:00}m" : $"in {Math.Max(1, (int)diff.TotalMinutes)}m";
        var late = -diff;
        if (late.TotalMinutes < 1) return "Due now";
        if (late.TotalHours < 1) return $"Overdue {(int)late.TotalMinutes}m";
        if (late.TotalDays < 1) return $"Overdue {(int)late.TotalHours}h {late.Minutes:00}m";
        return $"Overdue {(int)late.TotalDays}d";
    }

    /// <summary>True when an open task is more than a minute past its due time.</summary>
    public static bool IsOverdue(DateTime? due, bool done, DateTime now) => due is { } at && !done && (now - at).TotalMinutes >= 1;

    static readonly Regex TaskRelativePattern = new(@"\b(?:in|dalam)?\s*(\d+)\s*(m|min|mins|menit|h|hr|hours|jam)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Task12HourPattern = new(@"\b(?:at|jam|pukul)?\s*(\d{1,2})(?::(\d{2}))?\s*(am|pm)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Task24HourPattern = new(@"\b(?:at|jam|pukul)?\s*(\d{1,2}):(\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex TaskKeywordDigitsPattern = new(@"\b(?:at|jam|pukul)\s+(\d{1,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses quick-add task text and chip minutes against <paramref name="now"/> and an optional <paramref name="selectedDate"/>.
    /// Relative durations stay relative to <paramref name="now"/>; clock times anchor on <paramref name="selectedDate"/>;
    /// untimed entries on a date other than today become all-day tasks for that date.
    /// </summary>
    public static ParsedTaskInput ParseTaskInput(string input, DateTime now, int? explicitDueMinutes = null, DateTime? selectedDate = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return new ParsedTaskInput("", null, false);

        string cleanTitle = input.Trim();
        DateTime? due = null;
        bool allDay = false;
        DateTime baseDate = (selectedDate ?? now.Date).Date;
        bool isOtherDate = baseDate != now.Date;

        DateTime AnchorClock(TimeSpan time)
        {
            var target = baseDate.Add(time);
            if (!isOtherDate && target < now) target = target.AddDays(1);
            return target;
        }

        if (explicitDueMinutes is > 0)
        {
            due = now.AddMinutes(explicitDueMinutes.Value);
        }
        else if (explicitDueMinutes == 0)
        {
            if (isOtherDate)
            {
                due = baseDate;
                allDay = true;
            }
        }
        else
        {
            var inMatch = TaskRelativePattern.Match(cleanTitle);
            if (inMatch.Success)
            {
                int val = int.Parse(inMatch.Groups[1].Value);
                string unit = inMatch.Groups[2].Value.ToLowerInvariant();
                due = (unit.StartsWith("h") || unit == "jam") ? now.AddHours(val) : now.AddMinutes(val);
                cleanTitle = cleanTitle.Remove(inMatch.Index, inMatch.Length).Trim();
            }
            else if (Task12HourPattern.Match(cleanTitle) is { Success: true } ampmMatch)
            {
                int h = int.Parse(ampmMatch.Groups[1].Value);
                int m = ampmMatch.Groups[2].Success ? int.Parse(ampmMatch.Groups[2].Value) : 0;
                string meridiem = ampmMatch.Groups[3].Value.ToLowerInvariant();
                if (meridiem == "pm" && h < 12) h += 12;
                if (meridiem == "am" && h == 12) h = 0;
                if (h is >= 0 and <= 23 && m is >= 0 and <= 59)
                {
                    due = AnchorClock(new TimeSpan(h, m, 0));
                    cleanTitle = cleanTitle.Remove(ampmMatch.Index, ampmMatch.Length).Trim();
                }
            }
            else if (Task24HourPattern.Match(cleanTitle) is { Success: true } atMatch)
            {
                int h = int.Parse(atMatch.Groups[1].Value);
                int m = int.Parse(atMatch.Groups[2].Value);
                if (h is >= 0 and <= 23 && m is >= 0 and <= 59)
                {
                    due = AnchorClock(new TimeSpan(h, m, 0));
                    cleanTitle = cleanTitle.Remove(atMatch.Index, atMatch.Length).Trim();
                }
            }
            else if (TaskKeywordDigitsPattern.Match(cleanTitle) is { Success: true } wordMatch && TimeInput.TryParse(wordMatch.Groups[1].Value, out var wordTime))
            {
                due = AnchorClock(wordTime);
                cleanTitle = cleanTitle.Remove(wordMatch.Index, wordMatch.Length).Trim();
            }

            if (due is null && isOtherDate)
            {
                due = baseDate;
                allDay = true;
            }
        }

        string title = string.IsNullOrWhiteSpace(cleanTitle)
            ? input.Trim()
            : Regex.Replace(cleanTitle, @"\s{2,}", " ");
        return new ParsedTaskInput(title, due, allDay);
    }

    /// <summary>Formats an existing task back into composer text for inline editing.</summary>
    public static string FormatTaskForEdit(string title, DateTime? dueTime, bool allDay)
    {
        string t = (title ?? "").Trim();
        if (dueTime is { } due && !allDay)
            return t.Length == 0 ? due.ToString("HH:mm") : $"{t} {due:HH:mm}";
        return t;
    }

    /// <summary>
    /// Parses an edited task submission. If an overdue task from earlier today keeps its unchanged clock time,
    /// preserves the original due instant instead of rolling it to tomorrow.
    /// </summary>
    public static ParsedTaskInput ParseTaskEdit(string input, DateTime now, int? explicitDueMinutes, DateTime selectedDate, DateTime? originalDue, bool originalAllDay)
    {
        var parsed = ParseTaskInput(input, now, explicitDueMinutes, selectedDate);
        if (explicitDueMinutes is null &&
            originalDue is { } orig &&
            !originalAllDay &&
            selectedDate.Date == now.Date &&
            orig.Date == now.Date &&
            orig < now &&
            parsed.DueTime is { } newDue &&
            !parsed.AllDay &&
            newDue == orig.AddDays(1))
        {
            return parsed with { DueTime = orig };
        }
        return parsed;
    }

    /// <summary>Computes the updated due time and all-day flag for a task snooze or reschedule action.</summary>
    public static (DateTime? DueTime, bool AllDay) ComputeSnoozeTarget(DateTime? currentDue, bool currentAllDay, TaskSnoozePreset preset, DateTime now)
    {
        switch (preset)
        {
            case TaskSnoozePreset.Minutes10:
                return (now.AddMinutes(10), false);
            case TaskSnoozePreset.Minutes30:
                return (now.AddMinutes(30), false);
            case TaskSnoozePreset.Hour1:
                return (now.AddHours(1), false);
            case TaskSnoozePreset.Hours3:
                return (now.AddHours(3), false);
            case TaskSnoozePreset.Tomorrow:
                if (currentDue is { } tDue && !currentAllDay)
                    return (now.Date.AddDays(1).Add(tDue.TimeOfDay), false);
                return (now.Date.AddDays(1), true);
            case TaskSnoozePreset.NextWeek:
            {
                DateTime anchor = currentDue is { } d && d.Date > now.Date ? d.Date : now.Date;
                if (currentDue is { } wDue && !currentAllDay)
                    return (anchor.AddDays(7).Add(wDue.TimeOfDay), false);
                return (anchor.AddDays(7), true);
            }
            case TaskSnoozePreset.ClearDue:
            default:
                return (null, false);
        }
    }

    static string Minutes(double total)
    {
        int m = Math.Max(1, (int)Math.Ceiling(total));
        return m >= 60 ? $"{m / 60}h {m % 60:00}m" : $"{m} min";
    }

    /// <summary>Time column text for an agenda row: "All day", "10:00–11:00", or "10:00" for events without an end.</summary>
    public static string TimeText(AgendaEvent e)
    {
        if (e.AllDay) return "All day";
        return e.End > e.Start ? $"{e.Start:HH:mm}–{e.End:HH:mm}" : $"{e.Start:HH:mm}";
    }

    /// <summary>Accepts https:// and webcal:// feed links; returns the https URL to fetch, or null.</summary>
    public static Uri? NormalizeFeedUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (t.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) t = "https://" + t["webcal://".Length..];
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        return uri;
    }
}
