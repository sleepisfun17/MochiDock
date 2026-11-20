namespace MochiDock.Core;

public enum PowerNoticeKind { None, PluggedIn, Unplugged, LowBattery, CriticalBattery }

/// <summary>A battery reading. <paramref name="HasBattery"/> is false on desktops and when Windows reports no battery or an unknown level.</summary>
public readonly record struct PowerState(bool HasBattery, bool OnAc, int Percent);

/// <summary>
/// Decides when a charger / low-battery notice should be shown. Pure and clock-free so it can be unit-tested.
/// - The first reading never notifies (it only records the starting state).
/// - Plugging in or unplugging notifies once (unplugging while already low gives the low-battery warning instead); rapid flapping within <see cref="FlapWindow"/> is ignored.
/// - Low battery fires when running on battery and the level falls to 20% or below, critical at 10% or below, each once per descent.
///   They re-arm when the level recovers above 25% / 15% or the charger is connected.
/// </summary>
public sealed class PowerNoticePolicy
{
    public static readonly TimeSpan FlapWindow = TimeSpan.FromSeconds(2);
    public const int LowPercent = 20;
    public const int CriticalPercent = 10;

    PowerState? last;
    DateTimeOffset? lastPlugNotice;
    bool lowArmed = true, criticalArmed = true;

    public PowerNoticeKind Evaluate(PowerState state, DateTimeOffset now)
    {
        if (!state.HasBattery || state.Percent is < 0 or > 100)
        {
            last = null;
            return PowerNoticeKind.None;
        }

        var previous = last;
        last = state;

        if (state.OnAc)
        {
            lowArmed = criticalArmed = true;
        }
        else
        {
            if (state.Percent > LowPercent + 5) lowArmed = true;
            if (state.Percent > CriticalPercent + 5) criticalArmed = true;
        }

        if (previous is null)
        {
            // Starting state: do not announce it, and do not fire a low-battery notice for a level we merely joined at.
            if (!state.OnAc)
            {
                if (state.Percent <= LowPercent) lowArmed = false;
                if (state.Percent <= CriticalPercent) criticalArmed = false;
            }
            return PowerNoticeKind.None;
        }

        if (previous.Value.OnAc != state.OnAc)
        {
            if (lastPlugNotice is { } at && now - at < FlapWindow) return PowerNoticeKind.None;
            lastPlugNotice = now;
            if (state.OnAc) return PowerNoticeKind.PluggedIn;
            // Unplugging while already low: the warning is the useful message, not "On battery".
            if (state.Percent <= CriticalPercent && criticalArmed) { criticalArmed = lowArmed = false; return PowerNoticeKind.CriticalBattery; }
            if (state.Percent <= LowPercent && lowArmed) { lowArmed = false; return PowerNoticeKind.LowBattery; }
            return PowerNoticeKind.Unplugged;
        }

        if (state.OnAc) return PowerNoticeKind.None;

        if (state.Percent <= CriticalPercent && criticalArmed)
        {
            criticalArmed = false;
            lowArmed = false;
            return PowerNoticeKind.CriticalBattery;
        }
        if (state.Percent <= LowPercent && lowArmed)
        {
            lowArmed = false;
            return PowerNoticeKind.LowBattery;
        }
        return PowerNoticeKind.None;
    }

    public static string Text(PowerNoticeKind kind, int percent) => kind switch
    {
        PowerNoticeKind.PluggedIn => $"Charging · {percent}%",
        PowerNoticeKind.Unplugged => $"On battery · {percent}%",
        PowerNoticeKind.LowBattery => $"Battery low · {percent}%",
        PowerNoticeKind.CriticalBattery => $"Battery critical · {percent}%",
        _ => ""
    };

    /// <summary>Seconds the notice stays on the capsule: brief for plug events, longer for warnings.</summary>
    public static double DisplaySeconds(PowerNoticeKind kind) => kind is PowerNoticeKind.LowBattery or PowerNoticeKind.CriticalBattery ? 6 : 2.5;
}
