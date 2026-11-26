namespace MochiDock.Core;

/// <summary>Pure validation, naming, and theme-safe colour presets for multi-calendar feeds.</summary>
public static class CalendarFeedPolicy
{
    public const int MaxFeeds = 10;
    public const string DefaultColor = "Blue";
    public static readonly IReadOnlyList<string> ColorKeys = new[] { "Blue", "Green", "Orange", "Purple", "Red", "Accent" };

    public static string NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return DefaultColor;
        string trimmed = color.Trim();
        foreach (string key in ColorKeys)
            if (string.Equals(key, trimmed, StringComparison.OrdinalIgnoreCase))
                return key;
        return DefaultColor;
    }

    public static string BrushKey(string? color) => NormalizeColor(color) switch
    {
        "Green" => "IslandGreenBrush",
        "Orange" => "IslandOrangeBrush",
        "Purple" => "IslandPurpleBrush",
        "Red" => "IslandRedBrush",
        "Accent" => "IslandAccentBrush",
        _ => "IslandBlueBrush",
    };

    public static string PickNextColor(IEnumerable<string?> usedColors)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (string? raw in usedColors)
        {
            count++;
            used.Add(NormalizeColor(raw));
        }
        foreach (string key in ColorKeys)
            if (!used.Contains(key))
                return key;
        return ColorKeys[count % ColorKeys.Count];
    }

    public static string NormalizeName(string? name, int fallbackNumber = 1)
    {
        string collapsed = string.Join(' ', (name ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length == 0)
            return fallbackNumber <= 1 ? "Calendar" : $"Calendar {fallbackNumber}";
        return collapsed.Length <= 24 ? collapsed : collapsed[..24].TrimEnd();
    }

    public static string FormatDetail(string? calendarName, bool showCalendarName, string? focusText, string? place)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(focusText)) parts.Add(focusText.Trim());
        if (showCalendarName && !string.IsNullOrWhiteSpace(calendarName)) parts.Add(calendarName.Trim());
        if (!string.IsNullOrWhiteSpace(place)) parts.Add(place.Trim());
        return string.Join(" · ", parts);
    }
}
