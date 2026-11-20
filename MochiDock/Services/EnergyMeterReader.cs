using System.Runtime.InteropServices;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>
/// CPU package power from Windows' own "Energy Meter" performance counters (RAPL on Intel, and on AMD where Windows exposes it).
/// No third-party driver is needed, so it covers PCs where the PawnIO sensor driver is not installed. On an Intel CPU with integrated
/// graphics the package figure already includes the graphics. Unavailable counters simply return null.
/// </summary>
public sealed class EnergyMeterReader : IDisposable
{
    private const string PackagePath = @"\Energy Meter(RAPL_Package0_PKG)\Power";
    private const string DramPath = @"\Energy Meter(RAPL_Package0_DRAM)\Power";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private IntPtr _query, _package, _dram;
    private bool _opened, _disposed;
    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValue { public uint Status; public double Value; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(IntPtr source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);
    private const uint PdhFmtDouble = 0x00000200;

    /// <summary>Package and DRAM watts, each null when Windows does not provide that counter or it reads zero.</summary>
    public (double? PackageW, double? DramW) Sample()
    {
        lock (_gate)
        {
            if (_disposed) return (null, null);
            if (!_opened && !Open()) return (null, null);
            try
            {
                if (PdhCollectQueryData(_query) != 0) return (null, null);
                return (Read(_package), Read(_dram));
            }
            catch (Exception ex)
            {
                AppLog.Warn("Power", ex);
                return (null, null);
            }
        }
    }

    private bool Open()
    {
        if (DateTimeOffset.UtcNow < _nextAttempt) return false;
        try
        {
            if (PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _query) == 0 &&
                PdhAddEnglishCounter(_query, PackagePath, IntPtr.Zero, out _package) == 0)
            {
                // DRAM is optional: many CPUs do not report it.
                if (PdhAddEnglishCounter(_query, DramPath, IntPtr.Zero, out _dram) != 0) _dram = IntPtr.Zero;
                PdhCollectQueryData(_query); // the first sample only primes the counters
                _opened = true;
                return true;
            }
        }
        catch (Exception ex) { AppLog.Warn("Power", ex); }
        CloseQuery();
        _nextAttempt = DateTimeOffset.UtcNow + RetryDelay;
        AppLog.Warn("Power", "Windows Energy Meter counters are not available on this PC.");
        return false;
    }

    private static double? Read(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        if (PdhGetFormattedCounterValue(counter, PdhFmtDouble, out _, out var value) != 0 || value.Status != 0) return null;
        return EnergyMeterPower.ToWatts(value.Value);
    }

    private void CloseQuery()
    {
        try { if (_query != IntPtr.Zero) PdhCloseQuery(_query); } catch (Exception ex) { AppLog.Warn("Power", ex); }
        _query = _package = _dram = IntPtr.Zero;
        _opened = false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CloseQuery();
        }
    }
}
