using MochiDock.Core;
using LibreHardwareMonitor.Hardware;

namespace MochiDock.Services;

/// <summary>
/// Reads real CPU and GPU sensors (temperature, GPU clock and power) through LibreHardwareMonitorLib.
/// Reads are lazy: a refresh only starts when <see cref="Latest"/> is requested and the last one is stale,
/// so no background work runs unless the hardware card or alerts actually ask for data.
/// </summary>
public sealed class SensorService : IDisposable
{
    static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    /// <summary>The PawnIO driver address and the winget command that installs it (shown to the user; never run by the app).</summary>
    public const string PawnIoUrl = "https://pawnio.eu";
    public const string PawnIoWingetCommand = "winget install --id namazso.PawnIO --exact --source winget";

    /// <summary>Whether the optional PawnIO driver (real CPU temperature) is installed on this PC.</summary>
    public static bool IsPawnIoInstalled
    {
        get
        {
            try { return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled; }
            catch (Exception ex) { AppLog.Warn("Sensors", ex); return false; }
        }
    }

    readonly object _gate = new();
    Computer? _computer;
    SensorSnapshot _latest = SensorSnapshot.Empty;
    string? _gpuNameHint;
    int _refreshing;
    bool _disposed;
    DateTimeOffset _nextOpenAttempt = DateTimeOffset.MinValue;
    static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMinutes(1);

    /// <summary>The most recent snapshot. Never blocks; schedules a background refresh when stale.</summary>
    public SensorSnapshot Latest(string? gpuNameHint)
    {
        _gpuNameHint = gpuNameHint;
        var snapshot = Volatile.Read(ref _latest);
        if (!_disposed && DateTimeOffset.UtcNow >= _nextOpenAttempt && DateTimeOffset.UtcNow - snapshot.CapturedAt > RefreshInterval
            && Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0)
        {
            Task.Run(Refresh);
        }
        return snapshot;
    }

    void Refresh()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_computer is null && !Open()) return;

                var raw = new List<RawSensor>();
                foreach (var hardware in _computer!.Hardware) Collect(hardware, raw);
                Volatile.Write(ref _latest, SensorPicker.Pick(raw, _gpuNameHint, DateTimeOffset.UtcNow));
            }
        }
        catch (Exception ex) { AppLog.Warn("Sensors", ex); }
        finally { Volatile.Write(ref _refreshing, 0); }
    }

    bool Open()
    {
        try
        {
            if (!LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled)
                AppLog.Warn("Sensors", "PawnIO driver is not installed, so CPU temperature stays estimated. GPU sensors do not need it.");

            var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            computer.Open();
            _computer = computer;
            AppLog.Warn("Sensors", "Sensor reader started: " + string.Join(", ", computer.Hardware.Select(h => h.HardwareType + ":" + h.Name)));
            return true;
        }
        catch (Exception ex)
        {
            // Try again in a minute: the driver may have been installed or started since.
            _nextOpenAttempt = DateTimeOffset.UtcNow + OpenRetryDelay;
            AppLog.Warn("Sensors", ex);
            return false;
        }
    }

    static void Collect(IHardware hardware, List<RawSensor> output)
    {
        try { hardware.Update(); }
        catch (Exception ex) { AppLog.Warn("Sensors", $"Update failed for {hardware.Name}: {ex.Message}"); return; }

        foreach (var sensor in hardware.Sensors)
            output.Add(new RawSensor(hardware.HardwareType.ToString(), hardware.Name, sensor.SensorType.ToString(), sensor.Name, sensor.Value));
        foreach (var sub in hardware.SubHardware) Collect(sub, output);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            try { _computer?.Close(); } catch (Exception ex) { AppLog.Warn("Sensors", ex); }
            _computer = null;
        }
    }
}
