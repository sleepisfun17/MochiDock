namespace MochiDock.Core;
public sealed class PrayerReminderGate
{
    private DateTimeOffset? _previous;
    private readonly HashSet<string> _sent = new();
    public void Reset(DateTimeOffset now) => _previous = now;
    public bool Due(string key, DateTimeOffset at, DateTimeOffset now)
    {
        bool due = _previous.HasValue && now >= _previous && now-_previous <= TimeSpan.FromSeconds(60) && at > _previous && at <= now && now-at <= TimeSpan.FromSeconds(60);
        return due && _sent.Add(key);
    }
    public void Advance(DateTimeOffset now) { _previous=now; if(_sent.Count>512)_sent.Clear(); }
}
public readonly record struct DhuhaPeriod(DateTimeOffset Start, DateTimeOffset End, bool IsActive, TimeSpan Remaining);

public readonly record struct ActivePrayerPeriod(string Name, DateTimeOffset? Start, DateTimeOffset? End, TimeSpan Remaining, bool IsDhuha);

public static class PrayerTime
{
    public static readonly TimeSpan DhuhaStartOffset = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DhuhaEndLead = TimeSpan.FromMinutes(10);

    public static DateTimeOffset At(DateTime local, TimeZoneInfo zone)
    {
        local=DateTime.SpecifyKind(local,DateTimeKind.Unspecified);
        if(zone.IsInvalidTime(local)) throw new ArgumentException("Prayer time falls inside a daylight-saving gap.");
        return new DateTimeOffset(local,zone.GetUtcOffset(local));
    }

    /// <summary>Computes the Dhuha prayer window (Sunrise + 15 min to Dhuhr - 10 min); returns null if the window is inverted or empty.</summary>
    public static DhuhaPeriod? DhuhaWindow(DateTimeOffset sunrise, DateTimeOffset dhuhr, DateTimeOffset now)
    {
        var start = sunrise + DhuhaStartOffset;
        var end = dhuhr - DhuhaEndLead;
        if (end <= start) return null;
        bool active = now >= start && now < end;
        return new DhuhaPeriod(start, end, active, active ? end - now : TimeSpan.Zero);
    }

    /// <summary>Formats remaining Dhuha time as e.g. "2h 15m left" or "18m left", or "" when not active.</summary>
    public static string FormatDhuhaRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "";
        int totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return totalMinutes >= 60 ? $"{totalMinutes / 60}h {totalMinutes % 60}m left" : $"{totalMinutes}m left";
    }

    /// <summary>
    /// Returns the currently active prayer period (one of the 5 Fard prayers, or Dhuha between Sunrise+15m and Dhuhr-10m),
    /// or null during the brief prohibited gaps around sunrise and zenith.
    /// </summary>
    public static ActivePrayerPeriod? ActivePeriod(
        DateTimeOffset fajr,
        DateTimeOffset sunrise,
        DateTimeOffset dhuhr,
        DateTimeOffset asr,
        DateTimeOffset maghrib,
        DateTimeOffset isha,
        DateTimeOffset? tomorrowFajr,
        DateTimeOffset now)
    {
        if (now < fajr) return new ActivePrayerPeriod("Isha", null, fajr, fajr - now, IsDhuha: false);
        if (now < sunrise) return new ActivePrayerPeriod("Fajr", fajr, sunrise, sunrise - now, IsDhuha: false);
        if (now < dhuhr)
        {
            if (DhuhaWindow(sunrise, dhuhr, now) is { IsActive: true } d)
                return new ActivePrayerPeriod("Dhuha", d.Start, d.End, d.Remaining, IsDhuha: true);
            return null;
        }
        if (now < asr) return new ActivePrayerPeriod("Dhuhr", dhuhr, asr, asr - now, IsDhuha: false);
        if (now < maghrib) return new ActivePrayerPeriod("Asr", asr, maghrib, maghrib - now, IsDhuha: false);
        if (now < isha) return new ActivePrayerPeriod("Maghrib", maghrib, isha, isha - now, IsDhuha: false);

        var rem = tomorrowFajr is { } tf && tf > now ? tf - now : TimeSpan.Zero;
        return new ActivePrayerPeriod("Isha", isha, tomorrowFajr, rem, IsDhuha: false);
    }

    public static string NormalizeSound(string mode) => mode == "Tone" ? "Tone" : "Silent";
    public static string MigrateSound(bool enabled,string mode) => !enabled ? "Silent" : NormalizeSound(mode);
}
