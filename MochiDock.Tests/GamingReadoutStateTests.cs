using MochiDock.Core;

public class GamingReadoutStateTests
{
    static readonly TemperatureThresholds DefaultThresholds = new(
        SettingsValidation.DefaultCpuTempWarm,
        SettingsValidation.DefaultCpuTempHot,
        SettingsValidation.DefaultGpuTempWarm,
        SettingsValidation.DefaultGpuTempHot);

    [Fact]
    public void UnmeasuredTemperaturesStayHiddenAndNormal()
    {
        var state = new GamingReadoutState();
        var snap = state.Update(120, gpuTempC: 65, gpuTempMeasured: false, cpuTempC: 78, cpuTempMeasured: false, DefaultThresholds);

        Assert.Equal("120", snap.FpsText);
        Assert.Equal("000", snap.FpsCapacity);
        Assert.False(snap.GpuTemp.Visible);
        Assert.Equal(TemperatureSeverity.Normal, snap.GpuTemp.Severity);
        Assert.False(snap.CpuTemp.Visible);
        Assert.Equal(TemperatureSeverity.Normal, snap.CpuTemp.Severity);
    }

    [Fact]
    public void MeasuredTemperaturesShowIndependentlyAndHoldSlotOnDropout()
    {
        var state = new GamingReadoutState();

        // First tick: only GPU is measured.
        var first = state.Update(144, gpuTempC: 62.4, gpuTempMeasured: true, cpuTempC: 55, cpuTempMeasured: false, DefaultThresholds);
        Assert.True(first.GpuTemp.Visible);
        Assert.Equal("62°", first.GpuTemp.Text);
        Assert.Equal("00°", first.GpuTemp.Capacity);
        Assert.False(first.CpuTemp.Visible);

        // Second tick: CPU becomes measured, GPU drops out momentarily -> GPU keeps slot as "—°" in Normal ink.
        var second = state.Update(0, gpuTempC: 0, gpuTempMeasured: false, cpuTempC: 81.2, cpuTempMeasured: true, DefaultThresholds);
        Assert.Equal("—", second.FpsText);
        Assert.True(second.GpuTemp.Visible);
        Assert.Equal("—°", second.GpuTemp.Text);
        Assert.Equal(TemperatureSeverity.Normal, second.GpuTemp.Severity);
        Assert.True(second.CpuTemp.Visible);
        Assert.Equal("81°", second.CpuTemp.Text);
        Assert.Equal(TemperatureSeverity.Warm, second.CpuTemp.Severity);

        // Reset forgets sticky slots.
        state.Reset();
        var afterReset = state.Update(60, gpuTempC: 0, gpuTempMeasured: false, cpuTempC: 0, cpuTempMeasured: false, DefaultThresholds);
        Assert.False(afterReset.GpuTemp.Visible);
        Assert.False(afterReset.CpuTemp.Visible);
    }

    [Fact]
    public void HundredPlusTemperatureWidensCapacityUntilReset()
    {
        var state = new GamingReadoutState();

        var hot = state.Update(1200, gpuTempC: 101, gpuTempMeasured: true, cpuTempC: 100, cpuTempMeasured: true, DefaultThresholds);
        Assert.Equal("0000", hot.FpsCapacity);
        Assert.Equal("101°", hot.GpuTemp.Text);
        Assert.Equal("100°", hot.GpuTemp.Capacity);
        Assert.Equal("100°", hot.CpuTemp.Text);
        Assert.Equal("100°", hot.CpuTemp.Capacity);

        var cooled = state.Update(60, gpuTempC: 52, gpuTempMeasured: true, cpuTempC: 49, cpuTempMeasured: true, DefaultThresholds);
        Assert.Equal("0000", cooled.FpsCapacity);
        Assert.Equal("100°", cooled.GpuTemp.Capacity);
        Assert.Equal("100°", cooled.CpuTemp.Capacity);

        state.Reset();
        var fresh = state.Update(60, gpuTempC: 52, gpuTempMeasured: true, cpuTempC: 49, cpuTempMeasured: true, DefaultThresholds);
        Assert.Equal("000", fresh.FpsCapacity);
        Assert.Equal("00°", fresh.GpuTemp.Capacity);
        Assert.Equal("00°", fresh.CpuTemp.Capacity);
    }

    [Fact]
    public void CpuAndGpuSeverityEvaluateIndependently()
    {
        var state = new GamingReadoutState();
        var thresholds = new TemperatureThresholds(CpuWarm: 70, CpuHot: 88, GpuWarm: 65, GpuHot: 78);

        // Below warm -> both Normal.
        var normal = state.Update(90, 64, true, 69, true, thresholds);
        Assert.Equal(TemperatureSeverity.Normal, normal.GpuTemp.Severity);
        Assert.Equal(TemperatureSeverity.Normal, normal.CpuTemp.Severity);

        // At GPU hot, CPU warm -> GPU Red, CPU Orange.
        var mixed = state.Update(90, 78, true, 70, true, thresholds);
        Assert.Equal(TemperatureSeverity.Hot, mixed.GpuTemp.Severity);
        Assert.Equal(TemperatureSeverity.Warm, mixed.CpuTemp.Severity);

        // Equal limits: Hot wins at boundary.
        Assert.Equal(TemperatureSeverity.Hot, GamingReadoutState.EvaluateSeverity(80, measured: true, warmLimit: 80, hotLimit: 80));
        // Unmeasured always Normal even above hot.
        Assert.Equal(TemperatureSeverity.Normal, GamingReadoutState.EvaluateSeverity(99, measured: false, warmLimit: 70, hotLimit: 80));
    }

    [Fact]
    public void TempColorThresholdClampAndPairNormalization()
    {
        Assert.Equal(40, SettingsValidation.ClampTempColorThreshold(10, 75));
        Assert.Equal(100, SettingsValidation.ClampTempColorThreshold(150, 75));
        Assert.Equal(75, SettingsValidation.ClampTempColorThreshold(double.NaN, 75));
        Assert.Equal(80, SettingsValidation.ClampTempColorThreshold(double.PositiveInfinity, 80));

        var ordered = SettingsValidation.NormalizeTempColorPair(90, 75, 75, 85);
        Assert.Equal((90d, 90d), ordered);

        var defaults = SettingsValidation.NormalizeTempColorPair(double.NaN, double.NegativeInfinity, 70, 80);
        Assert.Equal((70d, 80d), defaults);
    }

    [Fact]
    public void LegacyTemperatureThresholdMigratesOnceWhenCustomized()
    {
        // Customized legacy 92 °C migrates both CPU & GPU Red limits to 92 while keeping Warm intact.
        var high = SettingsValidation.MigrateLegacyTempThresholds(92, 75, 85, 70, 80);
        Assert.Equal((75d, 92d, 70d, 92d), high);

        // Low customized legacy 65 °C migrates both Red limits to 65 and clamps Warm down to 65.
        var low = SettingsValidation.MigrateLegacyTempThresholds(65, 75, 85, 70, 80);
        Assert.Equal((65d, 65d, 65d, 65d), low);

        // Legacy default 85 °C preserves distinct CPU (75/85) and GPU (70/80) defaults.
        var def = SettingsValidation.MigrateLegacyTempThresholds(85, 75, 85, 70, 80);
        Assert.Equal((75d, 85d, 70d, 80d), def);

        // Already customized GPU hot limit is not overwritten by legacy threshold.
        var customGpu = SettingsValidation.MigrateLegacyTempThresholds(92, 75, 85, 68, 79);
        Assert.Equal((75d, 85d, 68d, 79d), customGpu);
    }
}
