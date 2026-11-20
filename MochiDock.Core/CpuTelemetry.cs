namespace MochiDock.Core;

public readonly record struct CpuTimeCounters(ulong Idle, ulong Kernel, ulong User);
public sealed record CpuTimeReading(double SystemPercent, double UserPercent, double IdlePercent)
{
    public double UsagePercent => SystemPercent + UserPercent;
}

public sealed class CpuTimeSampler
{
    CpuTimeCounters? previous;
    public void Reset() => previous = null;
    public CpuTimeReading? Sample(CpuTimeCounters current)
    {
        var last = previous;
        previous = current;
        if (last is not { } old || current.Idle < old.Idle || current.Kernel < old.Kernel || current.User < old.User) return null;
        var idle = current.Idle - old.Idle;
        var kernel = current.Kernel - old.Kernel;
        var user = current.User - old.User;
        // Windows includes idle ticks in kernel time. Exclude them from System.
        if (idle > kernel) return null;
        var total = (double)kernel + user;
        if (total <= 0) return null;
        return new((kernel - idle) * 100.0 / total, user * 100.0 / total, idle * 100.0 / total);
    }
}

public static class CpuClock
{
    public static double? Ghz(double baseMhz, double performancePercent)
    {
        if (!double.IsFinite(baseMhz) || baseMhz <= 0 || !double.IsFinite(performancePercent) || performancePercent < 0) return null;
        var ghz = baseMhz / 1000.0 * (performancePercent / 100.0);
        return double.IsFinite(ghz) ? ghz : null;
    }

    public static double CeilingGhz(double baseGhz, double? currentGhz = null, double previousCeiling = 0)
    {
        if (!double.IsFinite(baseGhz) || baseGhz <= 0) return Math.Max(4.5, previousCeiling);
        // Estimate boost ceiling from base clock: standard modern CPUs boost to at least 4.5 GHz, or 1.4x base
        double estimate = Math.Max(4.5, Math.Ceiling(baseGhz * 1.45 * 2.0) / 2.0);
        double target = Math.Max(estimate, previousCeiling);
        if (currentGhz.HasValue && double.IsFinite(currentGhz.Value) && currentGhz.Value > target - 0.1)
        {
            // Expand ceiling to allow headroom above current peak
            target = Math.Max(target, Math.Ceiling((currentGhz.Value + 0.2) * 5.0) / 5.0);
        }
        return target;
    }
}
