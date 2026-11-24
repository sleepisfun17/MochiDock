using System.Windows.Threading;
using MochiDock.Core;
using MochiDock.Models;
using MochiDock.Services;

namespace MochiDock.ViewModels;

// The taskbar readout (blocks beside the tray): the settings, the 1-second sampler timer and the blocks the widget draws.
public partial class MainViewModel
{
    // The sampler runs only while a system, memory/disk or network block is on; prayer, weather and battery blocks refresh when their own
    // data changes, with no polling. It pauses while the PC is locked or the display is off, and stops with the dock.
    private readonly DispatcherTimer _taskbarTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TaskbarSample _taskbarSample = new();
    private IReadOnlyList<TaskbarBlock> _taskbarBlocks = Array.Empty<TaskbarBlock>();
    private PrayerSchedule? _taskbarSchedule;
    private bool _taskbarStopped;

    /// <summary>The blocks the taskbar widget draws now (empty when none is on or has content).</summary>
    public IReadOnlyList<TaskbarBlock> TaskbarBlocks => _taskbarBlocks;

    /// <summary>Raised on the dispatcher after <see cref="TaskbarBlocks"/> changed.</summary>
    public event Action? TaskbarBlocksChanged;

    public bool TaskbarShowSystem { get => _settings.TaskbarShowSystem; set => SetTaskbarOption(_settings.TaskbarShowSystem, value, v => _settings.TaskbarShowSystem = v); }
    public bool TaskbarShowMemoryDisk { get => _settings.TaskbarShowMemoryDisk; set => SetTaskbarOption(_settings.TaskbarShowMemoryDisk, value, v => _settings.TaskbarShowMemoryDisk = v); }
    public bool TaskbarShowNetwork { get => _settings.TaskbarShowNetwork; set => SetTaskbarOption(_settings.TaskbarShowNetwork, value, v => _settings.TaskbarShowNetwork = v); }
    public bool TaskbarShowPrayer { get => _settings.TaskbarShowPrayer; set => SetTaskbarOption(_settings.TaskbarShowPrayer, value, v => _settings.TaskbarShowPrayer = v); }
    public bool TaskbarShowWeather { get => _settings.TaskbarShowWeather; set => SetTaskbarOption(_settings.TaskbarShowWeather, value, v => _settings.TaskbarShowWeather = v, nameof(TaskbarShowWeather)); }

    private void SetTaskbarOption(bool current, bool value, Action<bool> assign, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (current == value) return;
        assign(value);
        SettingsService.Save(_settings);
        OnPropertyChanged(name);
        ApplyTaskbarSampler();
        RefreshTaskbarBlocks();
    }

    /// <summary>Which blocks are on, with the alert thresholds. Prayer and weather also need their own feature on.</summary>
    private TaskbarOptions BuildTaskbarOptions() => new()
    {
        System = _settings.TaskbarShowSystem,
        MemoryDisk = _settings.TaskbarShowMemoryDisk,
        Network = _settings.TaskbarShowNetwork,
        Prayer = _settings.TaskbarShowPrayer && _settings.EnablePrayerTimes,
        Weather = _settings.TaskbarShowWeather && _settings.ShowWeatherTab,
        Batteries = _settings.EnableDeviceBatteries && _settings.ShowDeviceBatteriesOnTaskbar,
        CpuAlert = _settings.CpuAlertThreshold,
        GpuAlert = _settings.GpuAlertThreshold,
        TempAlert = _settings.TemperatureAlertThreshold,
    };

    private static TaskbarNeeds NeedsOf(TaskbarOptions o) =>
        (o.System ? TaskbarNeeds.System : 0) | (o.MemoryDisk ? TaskbarNeeds.MemoryDisk : 0) | (o.Network ? TaskbarNeeds.Network : 0);

    /// <summary>Hooks the data the prayer, weather and battery blocks follow, and the sampler timer. Called once from the constructor.</summary>
    private void InitializeTaskbar()
    {
        _taskbarTimer.Tick += (_, _) => TaskbarTick();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CurrentPrayerSchedule)) { HookTaskbarSchedule(); RefreshTaskbarBlocks(); }
            else if (e.PropertyName == nameof(WeatherInfo)) RefreshTaskbarBlocks();
        };
        DeviceBatteriesChanged += RefreshTaskbarBlocks;
        DeviceBatterySettingsChanged += RefreshTaskbarBlocks;
        SettingsChanged += _ => { ApplyTaskbarSampler(); RefreshTaskbarBlocks(); };
        HookTaskbarSchedule();
        ApplyTaskbarSampler();
        RefreshTaskbarBlocks();
    }

    // The next prayer changes inside the schedule object (its own countdown), so follow it there as well.
    private void HookTaskbarSchedule()
    {
        if (ReferenceEquals(_taskbarSchedule, CurrentPrayerSchedule)) return;
        if (_taskbarSchedule != null) _taskbarSchedule.PropertyChanged -= OnTaskbarScheduleChanged;
        _taskbarSchedule = CurrentPrayerSchedule;
        if (_taskbarSchedule != null) _taskbarSchedule.PropertyChanged += OnTaskbarScheduleChanged;
    }

    private void OnTaskbarScheduleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrayerSchedule.NextPrayer)) RefreshTaskbarBlocks();
    }

    /// <summary>Starts or stops the 1 s timer: it runs only while a sampled block is on and the PC is in use (not locked, display on).</summary>
    private void ApplyTaskbarSampler()
    {
        var options = BuildTaskbarOptions();
        bool wanted = options.NeedsSampler && !_taskbarStopped && !_isWorkstationLocked && _displayAwake;
        if (wanted && !_taskbarTimer.IsEnabled)
        {
            _hardwareService.ResetTaskbarSampling();   // the first readings after a pause must not be averaged over it
            _taskbarTimer.Start();
            TaskbarTick();
        }
        else if (!wanted)
        {
            _taskbarTimer.Stop();
            if (!options.NeedsSampler) _taskbarSample = new TaskbarSample();
        }
    }

    private void TaskbarTick()
    {
        try
        {
            var options = BuildTaskbarOptions();
            if (!options.NeedsSampler || _taskbarStopped) { _taskbarTimer.Stop(); return; }
            _taskbarSample = _hardwareService.SampleTaskbar(NeedsOf(options));
            RefreshTaskbarBlocks();
        }
        catch (Exception ex) { AppLog.Warn("Taskbar", ex); }
    }

    /// <summary>Rebuilds the blocks from the latest data and raises <see cref="TaskbarBlocksChanged"/> only when they differ.</summary>
    public void RefreshTaskbarBlocks()
    {
        var options = BuildTaskbarOptions();
        var snapshot = new TaskbarSnapshot
        {
            CpuPercent = _taskbarSample.CpuPercent, CpuTempC = _taskbarSample.CpuTempC, CpuTempMeasured = _taskbarSample.CpuTempMeasured,
            GpuPercent = _taskbarSample.GpuPercent, GpuTempC = _taskbarSample.GpuTempC,
            RamPercent = _taskbarSample.RamPercent, DiskPercent = _taskbarSample.DiskPercent,
            NetDownBytesPerSecond = _taskbarSample.NetDownBytesPerSecond, NetUpBytesPerSecond = _taskbarSample.NetUpBytesPerSecond,
            PrayerName = CurrentPrayerSchedule?.NextPrayer?.Name ?? "",
            PrayerTime = CurrentPrayerSchedule?.NextPrayer?.FormattedTime ?? "",
            WeatherTempC = _weatherInfo?.Temperature,
            WindMetersPerSecond = _weatherInfo?.WindSpeed,
            WeatherGlyph = _weatherInfo?.WeatherIcon ?? "",
            WeatherCondition = _weatherInfo?.Condition ?? "",
            Batteries = options.Batteries
                ? TaskbarDeviceBatteries.Select(r => new TaskbarBattery(Views.Cards.DeviceBatteryRowConverter.Glyph(r.Kind), r.Name, r.Via, r.Percent, r.Charging, r.Stale, r.ReadAtUtc)).ToArray()
                : Array.Empty<TaskbarBattery>(),
        };
        var blocks = TaskbarReadouts.Build(snapshot, options);
        if (blocks.SequenceEqual(_taskbarBlocks)) return;
        _taskbarBlocks = blocks;
        TaskbarBlocksChanged?.Invoke();
    }

    /// <summary>A left click on a taskbar block: open the matching card (Stats on the CPU, Memory or Network module, Prayer, Weather, or the Battery page).</summary>
    public void OpenTaskbarBlock(TaskbarBlockKind kind)
    {
        switch (kind)
        {
            case TaskbarBlockKind.System: OpenStatsModule("CPU"); break;
            case TaskbarBlockKind.MemoryDisk: OpenStatsModule("Memory"); break;
            case TaskbarBlockKind.Network: OpenStatsModule("Network"); break;
            case TaskbarBlockKind.Prayer: ShowPrayer(); break;
            case TaskbarBlockKind.Weather: ShowWeather(); break;
            case TaskbarBlockKind.Batteries: OpenBatteryModule(); break;
        }
    }

    private void OpenStatsModule(string module)
    {
        SelectSystemModule(module);
        NavigateManually(IslandCardType.Hardware);
        ShowHardware();
    }

    private void StopTaskbarSampler()
    {
        _taskbarStopped = true;
        _taskbarTimer.Stop();
    }
}
