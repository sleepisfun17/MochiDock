using System.Collections.ObjectModel;
using System.Windows.Threading;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.ViewModels;

// Wireless mouse/keyboard batteries: the service polls, this part publishes the list and raises the silent low-battery notice.
public partial class MainViewModel
{
    private DeviceBatteryService? _deviceBatteries;
    private readonly DeviceBatteryAlertPolicy _deviceAlertPolicy = new();
    private bool _noticeIsDevice;

    public ObservableCollection<DeviceBatteryReading> DeviceBatteries { get; } = new();
    public bool HasDeviceBatteries => DeviceBatteries.Count > 0;
    private bool _hasSystemBattery = MochiDock.Services.BatteryStatus.Read()?.HasBattery ?? false;
    /// <summary>True on a laptop (Windows reports a system battery); false on a desktop.</summary>
    public bool HasSystemBattery => _hasSystemBattery;
    /// <summary>Nothing to show on the Battery page (no system battery and no wireless device yet).</summary>
    public bool IsBatteryPageEmpty => !_hasSystemBattery && DeviceBatteries.Count == 0;

    private int _systemPercent = MochiDock.Services.BatteryStatus.Read()?.Percent ?? 0;
    private bool _systemCharging = MochiDock.Services.BatteryStatus.Read() is { HasBattery: true, OnAc: true, Percent: < 100 };
    private BatteryChipInfo BatteryChip => BatteryChipPolicy.Summary(_hasSystemBattery, _systemPercent, _systemCharging, DeviceBatteries);
    public string BatteryChipLabel => BatteryChip.Label;
    public string BatteryChipText => BatteryChip.Text;
    public double BatteryChipPercent => BatteryChip.Percent ?? 0;
    public bool BatteryChipCharging => BatteryChip.Charging;
    public bool BatteryChipLow => BatteryChip.Percent is <= 15 && !BatteryChip.Charging;
    private void NotifyBatteryChip()
    {
        OnPropertyChanged(nameof(BatteryChipLabel)); OnPropertyChanged(nameof(BatteryChipText)); OnPropertyChanged(nameof(BatteryChipPercent));
        OnPropertyChanged(nameof(BatteryChipCharging)); OnPropertyChanged(nameof(BatteryChipLow)); OnPropertyChanged(nameof(ShowPowerChip));
    }

    private void UpdateSystemPower(MochiDock.Core.PowerState state)
    {
        _systemPercent = state.Percent;
        _systemCharging = state is { HasBattery: true, OnAc: true, Percent: < 100 };
        UpdateHasSystemBattery(state.HasBattery);
        NotifyBatteryChip();
    }

    private void UpdateHasSystemBattery(bool value)
    {
        if (_hasSystemBattery == value) return;
        _hasSystemBattery = value;
        _powerHistory.Clear();   // laptop battery rate and desktop estimate are different measures
        LatestPower = null;
        _lastPowerSampleUtc = DateTime.MinValue;
        OnPropertyChanged(nameof(HasSystemBattery));
        OnPropertyChanged(nameof(IsBatteryPageEmpty));
    }
    /// <summary>Raised on the dispatcher after <see cref="DeviceBatteries"/> changed (used by the taskbar widget).</summary>
    public event Action? DeviceBatteriesChanged;
    /// <summary>Raised when either device-battery setting changed.</summary>
    public event Action? DeviceBatterySettingsChanged;

    public bool EnableDeviceBatteries
    {
        get => _settings.EnableDeviceBatteries;
        set
        {
            if (_settings.EnableDeviceBatteries == value) return;
            _settings.EnableDeviceBatteries = value;
            SettingsService.Save(_settings);
            OnPropertyChanged();
            ApplyDeviceBatteryConfiguration();
            DeviceBatterySettingsChanged?.Invoke();
        }
    }

    public bool ShowDeviceBatteriesOnTaskbar
    {
        get => _settings.ShowDeviceBatteriesOnTaskbar;
        set
        {
            if (_settings.ShowDeviceBatteriesOnTaskbar == value) return;
            _settings.ShowDeviceBatteriesOnTaskbar = value;
            SettingsService.Save(_settings);
            OnPropertyChanged();
            DeviceBatterySettingsChanged?.Invoke();
        }
    }

    public bool ShowDeviceBatteryBackground
    {
        get => _settings.ShowDeviceBatteryBackground;
        set
        {
            if (_settings.ShowDeviceBatteryBackground == value) return;
            _settings.ShowDeviceBatteryBackground = value;
            SettingsService.Save(_settings);
            OnPropertyChanged();
            DeviceBatterySettingsChanged?.Invoke();
        }
    }

    /// <summary>Devices shown on the taskbar widget and eligible for the low-battery notice (hidden ones stay on the Battery page).</summary>
    public IReadOnlyList<DeviceBatteryReading> TaskbarDeviceBatteries => DeviceBatteryVisibility.Visible(DeviceBatteries, _settings.HiddenDeviceBatteryKeys);
    public bool IsDeviceBatteryHidden(string key) => (_settings.HiddenDeviceBatteryKeys ?? new()).Contains(key, StringComparer.OrdinalIgnoreCase);

    public void SetDeviceBatteryHidden(string key, bool hidden)
    {
        var list = _settings.HiddenDeviceBatteryKeys ??= new();
        var present = list.Contains(key, StringComparer.OrdinalIgnoreCase);
        if (hidden == present) return;
        if (hidden) list.Add(key); else list.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        SettingsService.Save(_settings);
        DeviceBatteriesChanged?.Invoke();
        DeviceBatterySettingsChanged?.Invoke();
    }

    public void ConfigureDeviceBatteries(DeviceBatteryService service)
    {
        if (_deviceBatteries is not null) _deviceBatteries.Changed -= OnDeviceBatteriesPolled;
        _deviceBatteries = service;
        service.Changed += OnDeviceBatteriesPolled;
        ApplyDeviceBatteryConfiguration();
    }

    private void ApplyDeviceBatteryConfiguration() =>
        _deviceBatteries?.Configure(EnableDeviceBatteries, _isWorkstationLocked || !_displayAwake);

    /// <summary>Manual refresh (tray, taskbar widget, Battery page): scans now; ignored while a scan runs or the feature is off.</summary>
    public Task RefreshDeviceBatteries() => _deviceBatteries?.RefreshNow() ?? Task.CompletedTask;

    /// <summary>
    /// Total power for the Battery page and the power log. A laptop on battery: the measured discharge rate (whole-machine draw).
    /// Everything else (desktop, laptop on AC where the rate is the charge rate): an estimate from CPU, GPU and memory sensors plus
    /// a fixed allowance. Null when nothing can be read.
    /// </summary>
    public MochiDock.Core.PowerReading? SamplePower(bool laptop)
    {
        var (cpu, memory, gpu) = _hardwareService.SamplePower();
        var status = laptop ? MochiDock.Services.BatteryStatus.Read() : null;
        int? rate = laptop ? BatteryPowerReader.ReadRateMilliwatts() : null;
        return MochiDock.Core.PowerSampler.Choose(laptop, status?.OnAc ?? false, rate, cpu, gpu, memory, DateTimeOffset.UtcNow, _powerAllowance, _powerAllowanceNote);
    }

    // Total power, sampled on every hardware tick like the CPU chip (1 s with Stats open, 2 s otherwise; at most once a second while
    // a game makes the tick run every 0.3 s), so the Power chip and the Battery page share one history that keeps running while
    // other tabs are open. The slack keeps a tick that fires a few milliseconds early from being skipped.
    private readonly MochiDock.Core.PowerHistory _powerHistory = new(60);
    private DateTime _lastPowerSampleUtc = DateTime.MinValue;
    private static readonly TimeSpan PowerSampleInterval = TimeSpan.FromMilliseconds(900);

    /// <summary>The latest total-power reading; null before the first sample or when nothing can be read.</summary>
    public MochiDock.Core.PowerReading? LatestPower { get; private set; }
    /// <summary>The recent total-power samples, oldest first (up to 60: one to two minutes).</summary>
    public IReadOnlyList<double> PowerSamples => _powerHistory.Samples();
    /// <summary>Raised on the UI thread after each power sample.</summary>
    public event Action? PowerSampled;
    public string PowerChipText => MochiDock.Core.PowerChip.Text(LatestPower);
    public System.Windows.Media.PointCollection PowerChipSparkPoints { get; private set; } = new();
    /// <summary>The header chip shows power (watts and sparkline) instead of a battery percentage: a desktop with no wireless device to report.</summary>
    public bool ShowPowerChip => BatteryChip.Percent is null;

    /// <summary>Samples total power if the last sample is older than the interval, or right away when <paramref name="force"/> is set.</summary>
    public void SamplePowerTick(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastPowerSampleUtc < PowerSampleInterval) return;
        _lastPowerSampleUtc = now;
        // A sensor or driver failure must never take the hardware tick down: the chip just shows "-".
        try { LatestPower = SamplePower(_hasSystemBattery); }
        catch (Exception ex) { LatestPower = null; AppLog.Warn("Power", ex); }
        if (LatestPower != null) _powerHistory.Add(LatestPower.Watts);
        var points = new System.Windows.Media.PointCollection();
        foreach (var (x, y) in MochiDock.Core.PowerChip.SparkPoints(_powerHistory.Samples())) points.Add(new System.Windows.Point(x, y));
        points.Freeze();
        PowerChipSparkPoints = points;
        OnPropertyChanged(nameof(PowerChipText));
        OnPropertyChanged(nameof(PowerChipSparkPoints));
        PowerSampled?.Invoke();
    }

    private PowerLogService? _powerLog;
    public bool EnablePowerLogging
    {
        get => _settings.EnablePowerLogging;
        set
        {
            if (_settings.EnablePowerLogging == value) return;
            _settings.EnablePowerLogging = value;
            SettingsService.Save(_settings);
            OnPropertyChanged();
            ApplyPowerLogConfiguration();
            DeviceBatterySettingsChanged?.Invoke();
        }
    }

    /// <summary>The power log (session energy and history) for the Battery page; null before the app finished starting.</summary>
    public PowerLogService? PowerLog => _powerLog;

    public void ConfigurePowerLog(PowerLogService service)
    {
        _powerLog = service;
        RecomputePowerAllowance();
        ApplyPowerLogConfiguration();
        // First run (empty list): find the board, memory and drives in the background so the estimate reflects this PC.
        if ((_settings.PowerComponents?.Count ?? 0) == 0) _ = DetectPowerComponentsAsync();
    }

    private void ApplyPowerLogConfiguration() =>
        _powerLog?.Configure(EnablePowerLogging, () => SamplePower(_hasSystemBattery));

    /// <summary>Refresh after a device arrival/removal or resume (debounced by the service).</summary>
    public void RequestDeviceBatteryRefresh(TimeSpan delay) => _deviceBatteries?.RequestRefresh(delay);

    private void OnDeviceBatteriesPolled()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(() =>
        {
            var latest = _deviceBatteries?.Latest ?? Array.Empty<DeviceBatteryReading>();
            DeviceBatteries.Clear();
            foreach (var reading in latest) DeviceBatteries.Add(reading);
            OnPropertyChanged(nameof(HasDeviceBatteries));
            OnPropertyChanged(nameof(IsBatteryPageEmpty));
            NotifyBatteryChip();
            DeviceBatteriesChanged?.Invoke();
            ShowDeviceBatteryNotice(_deviceAlertPolicy.Evaluate(DeviceBatteryVisibility.Visible(latest, _settings.HiddenDeviceBatteryKeys), DateTimeOffset.UtcNow));
        });
    }

    // Silent capsule notice in the hardware-notice slot; never expands the island or touches LastActiveCardType.
    private void ShowDeviceBatteryNotice(DeviceBatteryReading? reading)
    {
        if (reading is null || !EnableDeviceBatteries) return;
        if (_hardwarePresentationSuppressed || _wasGaming || _isWorkstationLocked || !_displayAwake ||
            IsNotificationInMainPill || IsPrayerArrivalVisible || IsPrayerReminderVisible || !ShouldShowNotification("Hardware")) return;
        _noticeIsAi = false; _noticeIsAgenda = false; _noticeIsGame = false; _noticeIsDevice = true;
        _hardwareNoticeText = DeviceBatteryAlertPolicy.Text(reading);
        _hardwareNoticeUntil = DateTimeOffset.UtcNow.AddSeconds(8);
        _hardwareNoticeTimer.Stop(); _hardwareNoticeTimer.Start();
        NotifyHardwareNotice();
    }

    /// <summary>Deliberate user choice (taskbar click): open Hardware → Battery.</summary>
    public void OpenBatteryModule()
    {
        SelectSystemModule("Battery");
        NavigateManually(IslandCardType.Hardware);
        ShowHardware();
    }
}
