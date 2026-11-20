using System;
using System.Globalization;

namespace MochiDock.Core;

/// <summary>
/// Reads a time of day the way people type it. Every field that takes a clock time uses this, so they all accept the same forms:
/// "9" or "11" (the hour, so 11:00), "930" (9:30), "0900" or "2145" (HHmm), and "9:30", "09:30" or "9.30" (with a separator).
/// The result is 0:00 up to 23:59; "2400", "25" and "12:75" are rejected.
/// </summary>
public static class TimeInput
{
    public static bool TryParse(string? text, out TimeSpan time)
    {
        time = default;
        string t = (text ?? "").Trim();
        if (t.Length == 0) return false;

        int hour, minute;
        int separator = t.IndexOfAny(new[] { ':', '.' });
        if (separator >= 0)
        {
            string h = t[..separator], m = t[(separator + 1)..];
            if (!IsDigits(h, 1, 2) || !IsDigits(m, 2, 2)) return false;
            hour = int.Parse(h, CultureInfo.InvariantCulture);
            minute = int.Parse(m, CultureInfo.InvariantCulture);
        }
        else
        {
            if (!IsDigits(t, 1, 4)) return false;
            int number = int.Parse(t, CultureInfo.InvariantCulture);
            if (t.Length <= 2) { hour = number; minute = 0; }      // "11" is 11:00
            else { hour = number / 100; minute = number % 100; }   // "930" is 9:30, "0900" is 9:00
        }
        if (hour > 23 || minute > 59) return false;
        time = new TimeSpan(hour, minute, 0);
        return true;
    }

    /// <summary>The time as the fields show it: "09:00".</summary>
    public static string Format(TimeSpan time) => time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    private static bool IsDigits(string s, int min, int max)
    {
        if (s.Length < min || s.Length > max) return false;
        foreach (char c in s) if (c < '0' || c > '9') return false;
        return true;
    }
}
