namespace MochiDock.Core;

/// <summary>An app whose Windows notifications are forwarded to the Apps card. The AUMID is preferred; the name is the fallback.</summary>
public sealed record WatchedApp(string Name, string Aumid);

/// <summary>
/// Decides which Windows toast notifications reach the Apps card. Discord is never matched here: it has its own card.
/// </summary>
public static class AppNotificationFilter
{
    public const int RecentCap = 20;

    public static bool IsDiscord(string? name, string? aumid) =>
        (name ?? "").Contains("Discord", StringComparison.OrdinalIgnoreCase) || (aumid ?? "").Contains("Discord", StringComparison.OrdinalIgnoreCase);

    /// <summary>The watched entry this toast belongs to, or null. An entry with an AUMID matches only that AUMID; one without matches the display name.</summary>
    public static WatchedApp? Match(string? name, string? aumid, IReadOnlyList<WatchedApp>? watched)
    {
        if (watched is null || watched.Count == 0 || IsDiscord(name, aumid)) return null;
        string n = (name ?? "").Trim(), a = (aumid ?? "").Trim();
        foreach (var app in watched)
        {
            if (app is null) continue;
            if (!string.IsNullOrWhiteSpace(app.Aumid))
            {
                if (a.Length > 0 && string.Equals(app.Aumid.Trim(), a, StringComparison.OrdinalIgnoreCase)) return app;
            }
            else if (n.Length > 0 && string.Equals((app.Name ?? "").Trim(), n, StringComparison.OrdinalIgnoreCase)) return app;
        }
        return null;
    }

    /// <summary>The toast's text lines as a title and a message: the first non-empty line is the title, the rest form the message.</summary>
    public static (string Title, string Message) Split(IEnumerable<string?>? lines, string fallbackTitle)
    {
        var texts = (lines ?? []).Select(t => (t ?? "").Trim()).Where(t => t.Length > 0).ToList();
        if (texts.Count == 0) return (fallbackTitle, "");
        return (texts[0], string.Join("\n", texts.Skip(1)));
    }

    /// <summary>The recently seen apps with <paramref name="app"/> moved to the front, without duplicates, capped at <see cref="RecentCap"/>.</summary>
    public static List<WatchedApp> Remember(IEnumerable<WatchedApp> recent, WatchedApp app)
    {
        var list = new List<WatchedApp> { app };
        list.AddRange(recent.Where(r => !Same(r, app)));
        return list.Take(RecentCap).ToList();
    }

    /// <summary>The watched list with <paramref name="app"/> added, unless it is already there or is Discord.</summary>
    public static List<WatchedApp> Add(IEnumerable<WatchedApp>? watched, WatchedApp app)
    {
        var list = (watched ?? []).ToList();
        if (string.IsNullOrWhiteSpace(app.Name) && string.IsNullOrWhiteSpace(app.Aumid)) return list;
        if (IsDiscord(app.Name, app.Aumid) || list.Any(w => Same(w, app))) return list;
        list.Add(new WatchedApp((app.Name ?? "").Trim(), (app.Aumid ?? "").Trim()));
        return list;
    }

    public static bool Same(WatchedApp a, WatchedApp b) =>
        !string.IsNullOrWhiteSpace(a.Aumid) && !string.IsNullOrWhiteSpace(b.Aumid)
            ? string.Equals(a.Aumid.Trim(), b.Aumid.Trim(), StringComparison.OrdinalIgnoreCase)
            : string.Equals((a.Name ?? "").Trim(), (b.Name ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
}
