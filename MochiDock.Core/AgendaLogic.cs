using System.Text.RegularExpressions;

namespace MochiDock.Core;

public enum AgendaFocusKind { Now, Next }

/// <summary>Where a task belongs in the agenda of a selected day.</summary>
public enum AgendaTaskGroup { None, OnDay, Floating }

/// <summary>The event the agenda should call out: the one happening now, or the next one starting within the hour.</summary>
public sealed record AgendaFocus(AgendaEvent Event, AgendaFocusKind Kind, string Text);

public static class AgendaLogic
{
    static readonly string[] JoinHosts = { "meet.google.com", "zoom.us", "teams.microsoft.com", "teams.live.com", "webex.com" };
    static readonly Regex UrlPattern = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
