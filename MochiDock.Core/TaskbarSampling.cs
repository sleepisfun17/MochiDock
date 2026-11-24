namespace MochiDock.Core;

/// <summary>What the 1-second taskbar sampler read (null means not read or not available). No process list, no per-core data.</summary>
public sealed record TaskbarSample
{
    public double? CpuPercent { get; init; }
    public double? CpuTempC { get; init; }
    /// <summary>False when <see cref="CpuTempC"/> is the estimate (no sensor reading), so the readout shows "~" and never warns.</summary>
    public bool CpuTempMeasured { get; init; }
    public double? GpuPercent { get; init; }
    public double? GpuTempC { get; init; }
    public double? RamPercent { get; init; }
    public double? DiskPercent { get; init; }
    public double? NetDownBytesPerSecond { get; init; }
    public double? NetUpBytesPerSecond { get; init; }
}

/// <summary>Which parts the sampler has to read (a switched-off block costs nothing).</summary>
[Flags]
public enum TaskbarNeeds { None = 0, System = 1, MemoryDisk = 2, Network = 4 }

/// <summary>
/// CPU load from successive GetSystemTimes readings. The taskbar sampler owns its own instance: the 24-hour history sampler has another
/// (its readings are 15 s apart), so the two never disturb each other's deltas.
/// </summary>
public sealed class CpuLoadDelta
{
    private (ulong Idle, ulong Total)? _previous;

    /// <summary>Feed one reading (idle time and kernel + user time, where kernel time includes idle); returns the load since the last one, or null for the first.</summary>
    public double? Update(ulong idle, ulong total)
    {
        double? load = null;
        if (_previous is { } p && total > p.Total && idle >= p.Idle)
            load = Math.Clamp(100.0 * (1 - (double)(idle - p.Idle) / (total - p.Total)), 0, 100);
        _previous = (idle, total);
        return load;
    }

    /// <summary>Forget the last reading (after a pause, so the next load is not averaged over the gap).</summary>
    public void Reset() => _previous = null;
}

/// <summary>Bytes per second from two successive byte totals. Own state per instance, like <see cref="CpuLoadDelta"/>.</summary>
public sealed class ByteRateDelta
{
    private (double Seconds, long Received, long Sent)? _previous;

    /// <summary>
    /// Feed one reading (a monotonic time in seconds and the received and sent byte totals); returns the rates since the last one, or null for
    /// the first, for a counter that went down (an adapter came or went) or for a time that did not advance.
    /// </summary>
    public (double Down, double Up)? Update(double seconds, long received, long sent)
    {
        (double Down, double Up)? rate = null;
        if (_previous is { } p && seconds > p.Seconds && received >= p.Received && sent >= p.Sent)
        {
            double elapsed = seconds - p.Seconds;
            rate = ((received - p.Received) / elapsed, (sent - p.Sent) / elapsed);
        }
        _previous = (seconds, received, sent);
        return rate;
    }

    public void Reset() => _previous = null;
}

public static class TaskbarSampling
{
    /// <summary>The CPU temperature estimate used when no sensor reading exists (the same formula as the hardware card); display-only.</summary>
    public static double EstimateCpuTemperature(double cpuPercent) => Math.Round(42.0 + Math.Clamp(cpuPercent, 0, 100) * 0.38);

    /// <summary>Disk activity from the "% Idle Time" counter: 100 minus the idle percentage, clamped to 0 to 100.</summary>
    public static double DiskActivityFromIdle(double idlePercent) => Math.Clamp(100.0 - idlePercent, 0, 100);
}
