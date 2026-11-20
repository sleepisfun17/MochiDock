using System.Globalization;
using System.Text.RegularExpressions;

namespace MochiDock.Core;

/// <summary>How dates are written in the dock (Settings > Appearance &amp; Dock > Date format).</summary>
public enum DateStyle
{
    /// <summary>The Windows region's short date order and weekday names.</summary>
    Automatic,
    /// <summary>2026/11/19 木, with Japanese weekday names whatever the Windows language.</summary>
    YearFirst,
    /// <summary>Thu 19 Nov 2026.</summary>
    DayFirst,
    /// <summary>Thu, Nov 19, 2026.</summary>
    MonthFirst,
    /// <summary>2026-11-19 Thu.</summary>
    Iso,
}

/// <summary>
/// Writes dates for display in the chosen <see cref="DateStyle"/>: a full date with the weekday (Today card, header), a month title
/// (calendar) and a short day without the year (task times, prayer, power sessions). Only for display: logs and saved data keep their
/// own fixed formats. The current style is app-wide state set from the settings.
/// </summary>
public static class DateDisplay
{
    static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja-JP");

    public static DateStyle Current { get; private set; } = DateStyle.Automatic;

    /// <summary>Raised after <see cref="Set"/> changed the style, so views can redraw their dates.</summary>
    public static event Action? Changed;

    /// <summary>Sets the app-wide style; returns true (and raises <see cref="Changed"/>) when it differs from the current one.</summary>
    public static bool Set(DateStyle style)
    {
        if (style == Current) return false;
        Current = style;
        Changed?.Invoke();
        return true;
    }

    /// <summary>The saved setting text as a style; anything unknown or empty is <see cref="DateStyle.Automatic"/>.</summary>
    public static DateStyle Parse(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || char.IsDigit(text[0])) return DateStyle.Automatic;
        return Enum.TryParse<DateStyle>(text, true, out var style) && Enum.IsDefined(style) ? style : DateStyle.Automatic;
    }

    /// <summary>Full date with the weekday: "2026/11/19 木", "Thu 19 Nov 2026", "Thu, Nov 19, 2026", "2026-11-19 Thu".</summary>
    public static string Full(DateTime date, DateStyle? style = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return (style ?? Current) switch
        {
            DateStyle.YearFirst => date.ToString("yyyy'/'MM'/'dd ddd", Japanese),
            DateStyle.DayFirst => date.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture),
            DateStyle.MonthFirst => date.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture),
            DateStyle.Iso => date.ToString("yyyy'-'MM'-'dd ddd", CultureInfo.InvariantCulture),
            _ => YearComesFirst(culture)
                ? date.ToString(culture.DateTimeFormat.ShortDatePattern + " ddd", culture)
                : date.ToString("ddd " + culture.DateTimeFormat.ShortDatePattern, culture),
        };
    }

    /// <summary>Month title for the calendar: "2026年11月", "November 2026", "2026-11" or the region's year-month pattern.</summary>
    public static string Month(DateTime date, DateStyle? style = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return (style ?? Current) switch
        {
            DateStyle.YearFirst => date.ToString("yyyy'年'M'月'", Japanese),
            DateStyle.DayFirst or DateStyle.MonthFirst => date.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            DateStyle.Iso => date.ToString("yyyy'-'MM", CultureInfo.InvariantCulture),
            _ => date.ToString(culture.DateTimeFormat.YearMonthPattern, culture),
        };
    }

    /// <summary>Day without the year: "11/19", "19 Nov", "Nov 19", "11-19", or the region's short date without its year.</summary>
    public static string Short(DateTime date, DateStyle? style = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return (style ?? Current) switch
        {
            DateStyle.YearFirst => date.ToString("M'/'d", Japanese),
            DateStyle.DayFirst => date.ToString("d MMM", CultureInfo.InvariantCulture),
            DateStyle.MonthFirst => date.ToString("MMM d", CultureInfo.InvariantCulture),
            DateStyle.Iso => date.ToString("MM'-'dd", CultureInfo.InvariantCulture),
            _ => date.ToString(WithoutYear(culture.DateTimeFormat.ShortDatePattern), culture),
        };
    }

    /// <summary>Settings label for a style, with today's date as the example.</summary>
    public static string Label(DateStyle style, DateTime today, CultureInfo? culture = null) => style switch
    {
        DateStyle.Automatic => $"Automatic (Windows region) · {Full(today, style, culture)}",
        DateStyle.YearFirst => $"Year first, Japanese · {Full(today, style, culture)}",
        DateStyle.DayFirst => $"Day first · {Full(today, style, culture)}",
        DateStyle.MonthFirst => $"Month first · {Full(today, style, culture)}",
        _ => $"ISO · {Full(today, style, culture)}",
    };

    static bool YearComesFirst(CultureInfo culture) => culture.DateTimeFormat.ShortDatePattern.TrimStart().StartsWith('y');

    // "yyyy/MM/dd" -> "MM/dd", "M/d/yyyy" -> "M/d", "dd.MM.yyyy" -> "dd.MM"; a year in the middle falls back to month/day.
    static string WithoutYear(string pattern)
    {
        var trimmed = Regex.Replace(pattern, @"^\s*y+[^dM]*|[^dM]*y+\s*$", "");
        return trimmed.Contains('y') || trimmed.Length == 0 ? "M'/'d" : trimmed;
    }
}
