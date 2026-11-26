using System.Globalization;

namespace MochiDock.Core;

public enum TemperatureSeverity { Normal, Warm, Hot }

public readonly record struct TemperatureThresholds(double CpuWarm, double CpuHot, double GpuWarm, double GpuHot);

public readonly record struct TemperatureSlotSnapshot(bool Visible, string Text, string Capacity, TemperatureSeverity Severity);

public readonly record struct GamingReadoutSnapshot(
    string FpsText,
    string FpsCapacity,
    TemperatureSlotSnapshot GpuTemp,
    TemperatureSlotSnapshot CpuTemp);

/// <summary>
/// Pure anti-jitter slot and severity state for the compact gaming capsule readout:
/// FPS keeps its widest digit count during a session, measured GPU/CPU temperatures keep their slots on brief dropouts,
/// and measured temperatures step from Normal → Warm → Hot independently against their configured thresholds.
/// </summary>
public sealed class GamingReadoutState
{
    int _fpsDigits = 3;
    bool _gpuTempSeen, _gpuTempWide;
    bool _cpuTempSeen, _cpuTempWide;

    /// <summary>Forget widths and temperature slots remembered during a session.</summary>
    public void Reset()
    {
        _fpsDigits = 3;
        _gpuTempSeen = false;
        _gpuTempWide = false;
        _cpuTempSeen = false;
        _cpuTempWide = false;
    }

    /// <summary>
    /// Classifies a measured temperature against warm (orange) and hot (red) thresholds.
    /// Evaluates <paramref name="hotLimit"/> first so equal limits resolve to <see cref="TemperatureSeverity.Hot"/>.
    /// </summary>
    public static TemperatureSeverity EvaluateSeverity(double tempC, bool measured, double warmLimit, double hotLimit)
    {
        if (!measured || !double.IsFinite(tempC) || tempC <= 0) return TemperatureSeverity.Normal;
        return tempC >= hotLimit ? TemperatureSeverity.Hot
            : tempC >= warmLimit ? TemperatureSeverity.Warm
            : TemperatureSeverity.Normal;
    }

    public GamingReadoutSnapshot Update(
        int currentFps,
        double gpuTempC,
        bool gpuTempMeasured,
        double cpuTempC,
        bool cpuTempMeasured,
        TemperatureThresholds thresholds)
    {
        var (cpuWarm, cpuHot) = SettingsValidation.NormalizeTempColorPair(
            thresholds.CpuWarm, thresholds.CpuHot,
            SettingsValidation.DefaultCpuTempWarm, SettingsValidation.DefaultCpuTempHot);
        var (gpuWarm, gpuHot) = SettingsValidation.NormalizeTempColorPair(
            thresholds.GpuWarm, thresholds.GpuHot,
            SettingsValidation.DefaultGpuTempWarm, SettingsValidation.DefaultGpuTempHot);

        string fpsText = currentFps > 0 ? currentFps.ToString(CultureInfo.InvariantCulture) : "—";
        if (currentFps > 0) _fpsDigits = Math.Max(_fpsDigits, fpsText.Length);
        string fpsCapacity = new('0', _fpsDigits);

        var gpuTemp = UpdateTempSlot(gpuTempC, gpuTempMeasured, gpuWarm, gpuHot, ref _gpuTempSeen, ref _gpuTempWide);
        var cpuTemp = UpdateTempSlot(cpuTempC, cpuTempMeasured, cpuWarm, cpuHot, ref _cpuTempSeen, ref _cpuTempWide);
        return new GamingReadoutSnapshot(fpsText, fpsCapacity, gpuTemp, cpuTemp);
    }

    static TemperatureSlotSnapshot UpdateTempSlot(
        double tempC,
        bool measured,
        double warmLimit,
        double hotLimit,
        ref bool seen,
        ref bool wide)
    {
        bool valid = double.IsFinite(tempC) && tempC > 0 && measured;
        seen |= valid;
        wide |= valid && tempC >= 100;
        string text = valid ? $"{tempC.ToString("0", CultureInfo.InvariantCulture)}°" : "—°";
        string capacity = wide ? "100°" : "00°";
        var severity = EvaluateSeverity(tempC, valid, warmLimit, hotLimit);
        return new TemperatureSlotSnapshot(seen, text, capacity, severity);
    }
}
