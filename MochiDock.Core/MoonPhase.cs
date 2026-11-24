namespace MochiDock.Core;

/// <summary>Snapshot of the lunar phase computed from the mean synodic month (pure, offline).</summary>
public sealed record MoonState(
    double CycleFraction,
    double AgeDays,
    double Illumination,
    bool IsWaxing,
    string Name,
    int PhaseIndex)
{
    public int IlluminationPercent => (int)Math.Round(Math.Clamp(Illumination, 0, 1) * 100.0, MidpointRounding.AwayFromZero);
    public string Summary => $"{Name} \u00B7 {IlluminationPercent}%";
    public string DetailTooltip => $"{Name} \u00B7 {IlluminationPercent}% illuminated \u00B7 Day {Math.Clamp((int)Math.Floor(AgeDays) + 1, 1, 30)} of lunar cycle";
}

/// <summary>Astronomical lunar phase helper based on a J2000 reference new moon epoch.</summary>
public static class MoonPhase
{
    /// <summary>Mean synodic month length in days.</summary>
    public const double SynodicMonthDays = 29.530588853;

    // 2000-01-06 18:14 UTC: well-known J2000 reference new moon.
    private static readonly DateTimeOffset ReferenceNewMoonUtc = new(2000, 1, 6, 18, 14, 0, TimeSpan.Zero);

    public static MoonState At(DateTimeOffset utc)
    {
        double days = (utc.ToUniversalTime() - ReferenceNewMoonUtc).TotalDays;
        double age = ((days % SynodicMonthDays) + SynodicMonthDays) % SynodicMonthDays;
        double fraction = age / SynodicMonthDays;
        double illumination = (1.0 - Math.Cos(fraction * 2.0 * Math.PI)) * 0.5;
        bool isWaxing = fraction < 0.5;
        var (index, name) = Classify(fraction);
        return new MoonState(fraction, age, illumination, isWaxing, name, index);
    }

    private static (int Index, string Name) Classify(double fraction)
    {
        if (fraction < 0.033 || fraction >= 0.967) return (0, "New Moon");
        if (fraction < 0.217) return (1, "Waxing Crescent");
        if (fraction < 0.283) return (2, "First Quarter");
        if (fraction < 0.467) return (3, "Waxing Gibbous");
        if (fraction < 0.533) return (4, "Full Moon");
        if (fraction < 0.717) return (5, "Waning Gibbous");
        if (fraction < 0.783) return (6, "Last Quarter");
        return (7, "Waning Crescent");
    }
}
