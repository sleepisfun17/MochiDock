using System.Globalization;

namespace MochiDock.Core;

/// <summary>One 15-minute forecast step from Open-Meteo: when it starts (UTC), precipitation in mm and the WMO weather code.</summary>
public readonly record struct PrecipitationSlot(DateTimeOffset Start, double Millimetres, int WeatherCode);

public enum RainSoonKind { None, Rain, Snow, Thunderstorm }

/// <summary>Rain, snow or a thunderstorm about to start, and the minutes until it does.</summary>
public readonly record struct RainSoonNotice(RainSoonKind Kind, int Minutes)
{
    public string Text => Minutes <= 5
        ? $"{Name} starting soon"
        : $"{Name} in about {Minutes} min";

    private string Name => Kind switch { RainSoonKind.Snow => "Snow", RainSoonKind.Thunderstorm => "Thunderstorm", _ => "Rain" };
}

/// <summary>
/// Decides when the capsule says "Rain in about 20 min". Pure and clock-free: the caller passes the time.
/// It fires once when the next hour turns wet while it is dry now, and re-arms only after an hour without any expected rain,
/// so a shower that comes and goes does not repeat the notice.
/// </summary>
public sealed class RainSoonPolicy
{
    public static readonly TimeSpan Horizon = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan RearmAfter = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan SlotLength = TimeSpan.FromMinutes(15);
    public const double WetMillimetres = 0.2;

    private bool _armed = true;
    private DateTimeOffset? _lastWet;

    /// <summary>The kind of precipitation a WMO code describes, or a measured amount implies.</summary>
    public static RainSoonKind KindOf(int code, double millimetres) => code switch
    {
        >= 95 and <= 99 => RainSoonKind.Thunderstorm,
        (>= 71 and <= 77) or 85 or 86 => RainSoonKind.Snow,
        (>= 51 and <= 67) or (>= 80 and <= 82) => RainSoonKind.Rain,
        _ => millimetres >= WetMillimetres ? RainSoonKind.Rain : RainSoonKind.None,
    };

    /// <summary>Whether the slot covering <paramref name="now"/> is already wet.</summary>
    public static bool IsWetNow(DateTimeOffset now, IReadOnlyList<PrecipitationSlot> slots) =>
        slots.Any(s => s.Start <= now && now < s.Start + SlotLength && KindOf(s.WeatherCode, s.Millimetres) != RainSoonKind.None);

    /// <summary>The first wet slot starting within the next hour, while it is dry now; null otherwise.</summary>
    public static RainSoonNotice? Upcoming(DateTimeOffset now, IReadOnlyList<PrecipitationSlot> slots)
    {
        if (slots.Count == 0 || IsWetNow(now, slots)) return null;
        foreach (var slot in slots.OrderBy(s => s.Start))
        {
            if (slot.Start <= now || slot.Start - now > Horizon) continue;
            var kind = KindOf(slot.WeatherCode, slot.Millimetres);
            if (kind == RainSoonKind.None) continue;
            int minutes = (int)Math.Round((slot.Start - now).TotalMinutes / 5.0, MidpointRounding.AwayFromZero) * 5;
            return new RainSoonNotice(kind, Math.Max(0, minutes));
        }
        return null;
    }

    /// <summary>The notice to show now, or null. Call after every weather refresh.</summary>
    public RainSoonNotice? Evaluate(DateTimeOffset now, IReadOnlyList<PrecipitationSlot> slots)
    {
        bool wetNow = IsWetNow(now, slots);
        var upcoming = Upcoming(now, slots);
        if (wetNow || upcoming != null) _lastWet = now;
        else if (_lastWet is { } wet && now - wet >= RearmAfter) { _armed = true; _lastWet = null; }

        if (wetNow) { _armed = false; return null; }   // already raining: nothing to warn about, and no notice when it pauses briefly
        if (upcoming == null || !_armed) return null;
        _armed = false;
        return upcoming;
    }

    /// <summary>
    /// An Open-Meteo time ("2026-11-20T15:30") in the response's time zone (<paramref name="utcOffsetSeconds"/>) as an absolute time.
    /// Null when it cannot be read.
    /// </summary>
    public static DateTimeOffset? ParseTime(string? iso, int utcOffsetSeconds)
    {
        if (!DateTime.TryParseExact(iso, new[] { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd" }, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local)) return null;
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromSeconds(utcOffsetSeconds));
    }
}
