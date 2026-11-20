using System.Runtime.InteropServices;
using MochiDock.Core;

namespace MochiDock.Services;

public sealed record CpuTelemetrySnapshot(CpuTimeReading? Times, double? Ghz, double? BaseGhz, DateTimeOffset CapturedAt, string ClockStatus);

// Read by the existing hardware poller; no new timer, background loop or ETW session.
public sealed class CpuTelemetryReader : IDisposable
{
    readonly object gate = new();
    readonly CpuTimeSampler times = new();
    IntPtr query, performance;
    double? baseMhz;
    bool frequencyPrimed, disposed;
    DateTimeOffset retryAt;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] static extern uint GetActiveProcessorCount(ushort group);
    [StructLayout(LayoutKind.Sequential)]
    struct PowerInfo { public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState; }
    [DllImport("powrprof.dll")] static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputSize, [Out] PowerInfo[] output, uint outputSize);
    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)] static extern uint OpenQuery(IntPtr source, IntPtr data, out IntPtr query);
    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)] static extern uint AddCounter(IntPtr query, string path, IntPtr data, out IntPtr counter);
    [DllImport("pdh.dll", EntryPoint = "PdhCollectQueryData")] static extern uint Collect(IntPtr query);
    [StructLayout(LayoutKind.Explicit)]
    struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterValue")] static extern uint ReadCounter(IntPtr counter, uint format, IntPtr type, out CounterValue value);
    [DllImport("pdh.dll", EntryPoint = "PdhCloseQuery")] static extern uint CloseQuery(IntPtr query);

    public CpuTelemetrySnapshot Read()
    {
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (disposed) return new(null, null, null, now, "CPU sampling stopped.");
            CpuTimeReading? reading = null;
            // GetSystemTimes is group-scoped above 64 processors; do not present that as a machine-wide breakdown.
            try
            {
                if (GetActiveProcessorCount(0xffff) is > 0 and <= 64 && GetSystemTimes(out var idle, out var kernel, out var user))
                    reading = times.Sample(new(idle, kernel, user));
                else times.Reset();
            }
            catch { times.Reset(); }

            double? ghz = null;
            var status = "Processor performance counter unavailable.";
            try
            {
                if (query == IntPtr.Zero && now >= retryAt) OpenFrequency(now);
                if (query != IntPtr.Zero && Collect(query) == 0)
                {
                    if (frequencyPrimed && ReadCounter(performance, 0x200 | 0x8000, IntPtr.Zero, out var value) == 0 && value.Status <= 1 && baseMhz.HasValue)
                        ghz = CpuClock.Ghz(baseMhz.Value, value.Value);
                    frequencyPrimed = true;
                    status = ghz.HasValue ? "Windows-reported frequency: processor performance × reported base clock. This is not an instantaneous per-core sensor reading."
                        : "Waiting for a valid processor performance sample.";
                }
                else if (query != IntPtr.Zero) { CloseFrequency(); retryAt = now.AddMinutes(1); }
            }
            catch { CloseFrequency(); retryAt = now.AddMinutes(1); }
            return new(reading, ghz, baseMhz / 1000.0, now, status);
        }
    }

    void OpenFrequency(DateTimeOffset now)
    {
        retryAt = now.AddMinutes(1);
        var count = GetActiveProcessorCount(0xffff);
        if (count == 0 || count > 4096) return;
        var power = new PowerInfo[count];
        if (CallNtPowerInformation(11, IntPtr.Zero, 0, power, checked((uint)(power.Length * Marshal.SizeOf<PowerInfo>()))) != 0 || power[0].MaxMhz == 0) return;
        baseMhz = power[0].MaxMhz;
        if (OpenQuery(IntPtr.Zero, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return; }
        if (AddCounter(query, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out performance) != 0) CloseFrequency();
        // The first Read collection establishes the baseline; formatting starts on the next poll.
    }

    public void Reset()
    {
        lock (gate) { times.Reset(); frequencyPrimed = false; }
    }
    void CloseFrequency()
    {
        if (query != IntPtr.Zero) CloseQuery(query);
        query = performance = IntPtr.Zero;
        frequencyPrimed = false;
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; times.Reset(); CloseFrequency(); }
    }
}
