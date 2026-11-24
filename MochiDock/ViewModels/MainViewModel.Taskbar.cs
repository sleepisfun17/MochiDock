using System.Windows.Threading;
using MochiDock.Core;
using MochiDock.Models;
using MochiDock.Services;

namespace MochiDock.ViewModels;

// The taskbar readout (blocks on the left edge and beside the tray): the settings, the sampler timer and the blocks the widget draws.
public partial class MainViewModel
{
    // The sampler runs only while a CPU, GPU, memory, disk or network item is on; prayer, weather and battery blocks refresh when their own
    // data changes, with no polling. It pauses while the PC is locked or the display is off, and stops with the dock.
    private readonly DispatcherTimer _taskbarTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TaskbarSample _taskbarSample = new();
    private IReadOnlyList<TaskbarBlock> _taskbarLeftBlocks = Array.Empty<TaskbarBlock>();
    private IReadOnlyList<TaskbarBlock> _taskbarRightBlocks = Array.Empty<TaskbarBlock>();
    private IReadOnlyList<TaskbarBlock> _taskbarBlocks = Array.Empty<TaskbarBlock>();
    private PrayerSchedule? _taskbarSchedule;
    private bool _taskbarStopped;

    /// <summary>The blocks drawn on the left side of the taskbar.</summary>
    public IReadOnlyList<TaskbarBlock> TaskbarLeftBlocks => _taskbarLeftBlocks;

    /// <summary>The blocks drawn on the right side of the taskbar (beside the tray / after task buttons).</summary>
    public IReadOnlyList<TaskbarBlock> TaskbarRightBlocks => _taskbarRightBlocks;

    /// <summary>Combined list of active taskbar blocks (Left then Right).</summary>
    public IReadOnlyList<TaskbarBlock> TaskbarBlocks => _taskbarBlocks;

    /// <summary>Raised on the dispatcher after <see cref="TaskbarLeftBlocks"/> or <see cref="TaskbarRightBlocks"/> changed.</summary>
    public event Action? TaskbarBlocksChanged;

    /// <summary>Raised on the dispatcher after taskbar monitor, placement, zone, font, colour or formatting changed.</summary>
    public event Action? TaskbarStyleChanged;

    public bool TaskbarShowCpuLoad { get => _settings.TaskbarShowCpuLoad; set => SetTaskbarOption(_settings.TaskbarShowCpuLoad, value, v => _settings.TaskbarShowCpuLoad = v); }
    public bool TaskbarShowCpuTemp { get => _settings.TaskbarShowCpuTemp; set => SetTaskbarOption(_settings.TaskbarShowCpuTemp, value, v => _settings.TaskbarShowCpuTemp = v); }
    public bool TaskbarShowGpuLoad { get => _settings.TaskbarShowGpuLoad; set => SetTaskbarOption(_settings.TaskbarShowGpuLoad, value, v => _settings.TaskbarShowGpuLoad = v); }
    public bool TaskbarShowGpuTemp { get => _settings.TaskbarShowGpuTemp; set => SetTaskbarOption(_settings.TaskbarShowGpuTemp, value, v => _settings.TaskbarShowGpuTemp = v); }
    public bool TaskbarShowMemory { get => _settings.TaskbarShowMemory; set => SetTaskbarOption(_settings.TaskbarShowMemory, value, v => _settings.TaskbarShowMemory = v); }
    public bool TaskbarShowDisk { get => _settings.TaskbarShowDisk; set => SetTaskbarOption(_settings.TaskbarShowDisk, value, v => _settings.TaskbarShowDisk = v); }
    public bool TaskbarShowUpload { get => _settings.TaskbarShowUpload; set => SetTaskbarOption(_settings.TaskbarShowUpload, value, v => _settings.TaskbarShowUpload = v); }
    public bool TaskbarShowDownload { get => _settings.TaskbarShowDownload; set => SetTaskbarOption(_settings.TaskbarShowDownload, value, v => _settings.TaskbarShowDownload = v); }
    public bool TaskbarShowPrayer { get => _settings.TaskbarShowPrayer; set => SetTaskbarOption(_settings.TaskbarShowPrayer, value, v => _settings.TaskbarShowPrayer = v); }
    public bool TaskbarShowWeatherTemp { get => _settings.TaskbarShowWeatherTemp; set => SetTaskbarOption(_settings.TaskbarShowWeatherTemp, value, v => _settings.TaskbarShowWeatherTemp = v); }
    public bool TaskbarShowWeatherWind { get => _settings.TaskbarShowWeatherWind; set => SetTaskbarOption(_settings.TaskbarShowWeatherWind, value, v => _settings.TaskbarShowWeatherWind = v); }

    public IReadOnlyList<TaskbarBlockKind> TaskbarBlockOrder => TaskbarReadouts.NormalizeOrder(_settings.TaskbarBlockOrder);

    public TaskbarZone GetTaskbarBlockZone(TaskbarBlockKind kind)
    {
        var map = TaskbarReadouts.ParseZones(_settings.TaskbarBlockZones);
        return TaskbarReadouts.ZoneOf(map, kind);
    }

    public void SetTaskbarBlockZone(TaskbarBlockKind kind, TaskbarZone zone)
    {
        if (GetTaskbarBlockZone(kind) == zone) return;
        _settings.TaskbarBlockZones ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _settings.TaskbarBlockZones[kind.ToString()] = TaskbarStyle.FormatZone(zone);
        SettingsService.Save(_settings);
        RefreshTaskbarBlocks();
        TaskbarStyleChanged?.Invoke();
    }

    public string TaskbarMonitor
    {
        get => _settings.TaskbarMonitor ?? "";
        set
        {
            string trimmed = value?.Trim() ?? "";
            if (string.Equals(_settings.TaskbarMonitor, trimmed, StringComparison.OrdinalIgnoreCase)) return;
            _settings.TaskbarMonitor = trimmed;
            SaveTaskbarStyle(nameof(TaskbarMonitor));
        }
    }

    public string TaskbarPlacement
    {
        get => TaskbarStyle.FormatPlacement(CurrentTaskbarPlacement);
        set
        {
            string normalized = TaskbarStyle.FormatPlacement(TaskbarStyle.ParsePlacement(value));
            if (string.Equals(_settings.TaskbarPlacement, normalized, StringComparison.Ordinal)) return;
            _settings.TaskbarPlacement = normalized;
            SaveTaskbarStyle(nameof(TaskbarPlacement));
        }
    }

    public int TaskbarOffsetPx
    {
        get => EffectiveTaskbarOffsetPx;
        set
        {
            int clamped = TaskbarStyle.ClampOffset(value);
            if (_settings.TaskbarOffsetPx == clamped) return;
            _settings.TaskbarOffsetPx = clamped;
            SaveTaskbarStyle(nameof(TaskbarOffsetPx));
        }
    }

    public int TaskbarLeftOffsetPx
    {
        get => EffectiveTaskbarLeftOffsetPx;
        set
        {
            int clamped = TaskbarStyle.ClampOffset(value);
            if (_settings.TaskbarLeftOffsetPx == clamped) return;
            _settings.TaskbarLeftOffsetPx = clamped;
            SaveTaskbarStyle(nameof(TaskbarLeftOffsetPx));
        }
    }

    public int TaskbarVerticalOffsetPx
    {
        get => EffectiveTaskbarVerticalOffsetPx;
        set
        {
            int clamped = TaskbarStyle.ClampVerticalOffset(value);
            if (_settings.TaskbarVerticalOffsetPx == clamped) return;
            _settings.TaskbarVerticalOffsetPx = clamped;
            SaveTaskbarStyle(nameof(TaskbarVerticalOffsetPx));
        }
    }

    public string TaskbarFontFamily
    {
        get => _settings.TaskbarFontFamily ?? "";
        set
        {
            string trimmed = value?.Trim() ?? "";
            if (string.Equals(_settings.TaskbarFontFamily, trimmed, StringComparison.Ordinal)) return;
            _settings.TaskbarFontFamily = trimmed;
            SaveTaskbarStyle(nameof(TaskbarFontFamily));
        }
    }

    public double TaskbarFontSize
    {
        get => TaskbarStyle.ClampFontSize(_settings.TaskbarFontSize);
        set
        {
            double clamped = TaskbarStyle.ClampFontSize(value);
            if (Math.Abs(_settings.TaskbarFontSize - clamped) < 0.01) return;
            _settings.TaskbarFontSize = clamped;
            SaveTaskbarStyle(nameof(TaskbarFontSize));
        }
    }

    public bool TaskbarFontBold
    {
        get => _settings.TaskbarFontBold;
        set
        {
            if (_settings.TaskbarFontBold == value) return;
            _settings.TaskbarFontBold = value;
            SaveTaskbarStyle(nameof(TaskbarFontBold));
        }
    }

    public int TaskbarBlockSpacing
    {
        get => TaskbarStyle.ClampBlockSpacing(_settings.TaskbarBlockSpacing);
        set
        {
            int clamped = TaskbarStyle.ClampBlockSpacing(value);
            if (_settings.TaskbarBlockSpacing == clamped) return;
            _settings.TaskbarBlockSpacing = clamped;
            SaveTaskbarStyle(nameof(TaskbarBlockSpacing));
        }
    }

    public bool TaskbarStableWidth
    {
        get => _settings.TaskbarStableWidth;
        set
        {
            if (_settings.TaskbarStableWidth == value) return;
            _settings.TaskbarStableWidth = value;
            SaveTaskbarStyle(nameof(TaskbarStableWidth));
        }
    }

    public string TaskbarLabelColor
    {
        get => TaskbarStyle.NormalizeHexColor(_settings.TaskbarLabelColor);
        set
        {
            string normalized = TaskbarStyle.NormalizeHexColor(value);
            if (string.Equals(_settings.TaskbarLabelColor, normalized, StringComparison.Ordinal)) return;
            _settings.TaskbarLabelColor = normalized;
            SaveTaskbarStyle(nameof(TaskbarLabelColor));
        }
    }

    public string TaskbarValueColor
    {
        get => TaskbarStyle.NormalizeHexColor(_settings.TaskbarValueColor);
        set
        {
            string normalized = TaskbarStyle.NormalizeHexColor(value);
            if (string.Equals(_settings.TaskbarValueColor, normalized, StringComparison.Ordinal)) return;
            _settings.TaskbarValueColor = normalized;
            SaveTaskbarStyle(nameof(TaskbarValueColor));
        }
    }

    public string TaskbarNetworkArrows
    {
        get => TaskbarStyle.FormatArrowStyle(TaskbarStyle.ParseArrowStyle(_settings.TaskbarNetworkArrows));
        set
        {
            string normalized = TaskbarStyle.FormatArrowStyle(TaskbarStyle.ParseArrowStyle(value));
            if (string.Equals(_settings.TaskbarNetworkArrows, normalized, StringComparison.Ordinal)) return;
            _settings.TaskbarNetworkArrows = normalized;
            SettingsService.Save(_settings);
            OnPropertyChanged(nameof(TaskbarNetworkArrows));
            RefreshTaskbarBlocks();
            TaskbarStyleChanged?.Invoke();
        }
    }

    public string TaskbarNetworkUnit
    {
        get => TaskbarStyle.FormatNetworkUnit(TaskbarStyle.ParseNetworkUnit(_settings.TaskbarNetworkUnit));
        set
        {
            string normalized = TaskbarStyle.FormatNetworkUnit(TaskbarStyle.ParseNetworkUnit(value));
            if (string.Equals(_settings.TaskbarNetworkUnit, normalized, StringComparison.Ordinal)) return;
            _settings.TaskbarNetworkUnit = normalized;
            SettingsService.Save(_settings);
            OnPropertyChanged(nameof(TaskbarNetworkUnit));
            RefreshTaskbarBlocks();
            TaskbarStyleChanged?.Invoke();
        }
    }

    public bool TaskbarSwapNetworkLines
    {
        get => _settings.TaskbarSwapNetworkLines;
        set
        {
            if (_settings.TaskbarSwapNetworkLines == value) return;
            _settings.TaskbarSwapNetworkLines = value;
            SettingsService.Save(_settings);
            OnPropertyChanged(nameof(TaskbarSwapNetworkLines));
            RefreshTaskbarBlocks();
            TaskbarStyleChanged?.Invoke();
        }
    }

    public bool TaskbarCompactLabels
    {
        get => _settings.TaskbarCompactLabels;
        set
        {
            if (_settings.TaskbarCompactLabels == value) return;
            _settings.TaskbarCompactLabels = value;
            SettingsService.Save(_settings);
            OnPropertyChanged(nameof(TaskbarCompactLabels));
            RefreshTaskbarBlocks();
            TaskbarStyleChanged?.Invoke();
        }
    }

    public int TaskbarRefreshSeconds
    {
        get => TaskbarStyle.ClampRefreshSeconds(_settings.TaskbarRefreshSeconds);
        set
        {
            int clamped = TaskbarStyle.ClampRefreshSeconds(value);
            if (_settings.TaskbarRefreshSeconds == clamped) return;
            _settings.TaskbarRefreshSeconds = clamped;
            SettingsService.Save(_settings);
            ApplyTaskbarRefreshInterval();
            OnPropertyChanged(nameof(TaskbarRefreshSeconds));
        }
    }

    public string TaskbarBatteryLayout
    {
        get => TaskbarStyle.FormatBatteryLayout(TaskbarStyle.ParseBatteryLayout(_settings.TaskbarBatteryLayout));
        set
        {
            string normalized = TaskbarStyle.FormatBatteryLayout(TaskbarStyle.ParseBatteryLayout(value));
            if (string.Equals(_settings.TaskbarBatteryLayout, normalized, StringComparison.Ordinal)) return;
            _settings.TaskbarBatteryLayout = normalized;
            SettingsService.Save(_settings);
            OnPropertyChanged(nameof(TaskbarBatteryLayout));
            RefreshTaskbarBlocks();
            TaskbarStyleChanged?.Invoke();
        }
    }

    public string TaskbarPrayerDisplay
    {
        get => TaskbarStyle.FormatPrayerDisplay(TaskbarStyle.ParsePrayerDisplay(_settings.TaskbarPrayerDisplay));
        set
        {
            string normalized = TaskbarStyle.FormatPrayerDisplay(TaskbarStyle.ParsePrayerDisplay(value));
            if (string.Equals(_settings.TaskbarPrayerDisplay, normalized, StringComparison.Ordinal)) return;
            _settings.TaskbarPrayerDisplay = normalized;
            SettingsService.Save(_settings);
            OnPropertyChanged(nameof(TaskbarPrayerDisplay));
            RefreshTaskbarBlocks();
            TaskbarStyleChanged?.Invoke();
        }
    }

    public TaskbarPlacementMode CurrentTaskbarPlacement => TaskbarStyle.ParsePlacement(_settings.TaskbarPlacement);
    public int EffectiveTaskbarOffsetPx => TaskbarStyle.ClampOffset(_settings.TaskbarOffsetPx);
    public int EffectiveTaskbarLeftOffsetPx => TaskbarStyle.ClampOffset(_settings.TaskbarLeftOffsetPx);
    public int EffectiveTaskbarVerticalOffsetPx => TaskbarStyle.ClampVerticalOffset(_settings.TaskbarVerticalOffsetPx);

    public TaskbarAppearance CurrentTaskbarAppearance => new()
    {
        FontFamily = _settings.TaskbarFontFamily ?? "",
        FontSize = TaskbarStyle.ClampFontSize(_settings.TaskbarFontSize),
        Bold = _settings.TaskbarFontBold,
        LabelColorHex = TaskbarStyle.NormalizeHexColor(_settings.TaskbarLabelColor),
        ValueColorHex = TaskbarStyle.NormalizeHexColor(_settings.TaskbarValueColor),
        BlockSpacing = TaskbarStyle.ClampBlockSpacing(_settings.TaskbarBlockSpacing),
        StableWidth = _settings.TaskbarStableWidth,
        BatteryLayout = TaskbarStyle.ParseBatteryLayout(_settings.TaskbarBatteryLayout),
    };

    public void MoveTaskbarBlock(TaskbarBlockKind kind, int delta)
    {
        var next = TaskbarReadouts.MoveBlock(TaskbarBlockOrder, kind, delta);
        if (next.SequenceEqual(TaskbarBlockOrder)) return;
        _settings.TaskbarBlockOrder = next.Select(k => k.ToString()).ToList();
        SettingsService.Save(_settings);
        OnPropertyChanged(nameof(TaskbarBlockOrder));
        RefreshTaskbarBlocks();
        TaskbarStyleChanged?.Invoke();
    }

    private void SaveTaskbarStyle(string propertyName)
    {
        SettingsService.Save(_settings);
        OnPropertyChanged(propertyName);
        TaskbarStyleChanged?.Invoke();
    }

    private void SetTaskbarOption(bool current, bool value, Action<bool> assign, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (current == value) return;
        assign(value);
        SettingsService.Save(_settings);
        OnPropertyChanged(name);
        ApplyTaskbarSampler();
        RefreshTaskbarBlocks();
    }

    /// <summary>Which items are on, their block order, zone, formatting and alert thresholds.</summary>
    private TaskbarOptions BuildTaskbarOptions() => new()
    {
        CpuLoad = _settings.TaskbarShowCpuLoad,
        CpuTemp = _settings.TaskbarShowCpuTemp,
        GpuLoad = _settings.TaskbarShowGpuLoad,
        GpuTemp = _settings.TaskbarShowGpuTemp,
        Memory = _settings.TaskbarShowMemory,
        Disk = _settings.TaskbarShowDisk,
        Upload = _settings.TaskbarShowUpload,
        Download = _settings.TaskbarShowDownload,
        Prayer = _settings.TaskbarShowPrayer && _settings.EnablePrayerTimes,
        WeatherTemp = _settings.TaskbarShowWeatherTemp && _settings.ShowWeatherTab,
        WeatherWind = _settings.TaskbarShowWeatherWind && _settings.ShowWeatherTab,
        Batteries = _settings.EnableDeviceBatteries && _settings.ShowDeviceBatteriesOnTaskbar,
        BlockOrder = TaskbarReadouts.NormalizeOrder(_settings.TaskbarBlockOrder),
        BlockZones = TaskbarReadouts.ParseZones(_settings.TaskbarBlockZones),
        NetworkArrows = TaskbarStyle.ParseArrowStyle(_settings.TaskbarNetworkArrows),
        NetworkUnit = TaskbarStyle.ParseNetworkUnit(_settings.TaskbarNetworkUnit),
        SwapNetworkLines = _settings.TaskbarSwapNetworkLines,
        CompactLabels = _settings.TaskbarCompactLabels,
        BatteryLayout = TaskbarStyle.ParseBatteryLayout(_settings.TaskbarBatteryLayout),
        PrayerDisplay = TaskbarStyle.ParsePrayerDisplay(_settings.TaskbarPrayerDisplay),
        PrayerWarnMinutes = 10,
        CpuAlert = _settings.CpuAlertThreshold,
        GpuAlert = _settings.GpuAlertThreshold,
        TempAlert = _settings.TemperatureAlertThreshold,
    };

    /// <summary>Builds Left and Right preview blocks using live data when present and sample values for unavailable items.</summary>
    public (IReadOnlyList<TaskbarBlock> Left, IReadOnlyList<TaskbarBlock> Right) BuildTaskbarPreview(TaskbarSnapshot sampleFallback)
    {
        var options = BuildTaskbarOptions();
        var live = BuildLiveTaskbarSnapshot(options);
        var merged = new TaskbarSnapshot
        {
            CpuPercent = live.CpuPercent ?? sampleFallback.CpuPercent,
            CpuTempC = live.CpuTempC ?? sampleFallback.CpuTempC,
            CpuTempMeasured = live.CpuTempC is not null ? live.CpuTempMeasured : sampleFallback.CpuTempMeasured,
            GpuPercent = live.GpuPercent ?? sampleFallback.GpuPercent,
            GpuTempC = live.GpuTempC ?? sampleFallback.GpuTempC,
            RamPercent = live.RamPercent ?? sampleFallback.RamPercent,
            DiskPercent = live.DiskPercent ?? sampleFallback.DiskPercent,
            NetUpBytesPerSecond = live.NetUpBytesPerSecond ?? sampleFallback.NetUpBytesPerSecond,
            NetDownBytesPerSecond = live.NetDownBytesPerSecond ?? sampleFallback.NetDownBytesPerSecond,
            PrayerName = !string.IsNullOrWhiteSpace(live.PrayerName) ? live.PrayerName : sampleFallback.PrayerName,
            PrayerTime = !string.IsNullOrWhiteSpace(live.PrayerTime) ? live.PrayerTime : sampleFallback.PrayerTime,
            PrayerRemaining = live.PrayerRemaining ?? sampleFallback.PrayerRemaining,
            WeatherTempC = live.WeatherTempC ?? sampleFallback.WeatherTempC,
            WindMetersPerSecond = live.WindMetersPerSecond ?? sampleFallback.WindMetersPerSecond,
            WeatherGlyph = !string.IsNullOrWhiteSpace(live.WeatherGlyph) ? live.WeatherGlyph : sampleFallback.WeatherGlyph,
            WeatherCondition = !string.IsNullOrWhiteSpace(live.WeatherCondition) ? live.WeatherCondition : sampleFallback.WeatherCondition,
            Batteries = live.Batteries.Count > 0 ? live.Batteries : sampleFallback.Batteries,
        };
        return TaskbarReadouts.BuildSplit(merged, options);
    }

    private void ApplyTaskbarRefreshInterval() =>
        _taskbarTimer.Interval = TimeSpan.FromSeconds(TaskbarStyle.ClampRefreshSeconds(_settings.TaskbarRefreshSeconds));

    /// <summary>Hooks the data the prayer, weather and battery blocks follow, and the sampler timer. Called once from the constructor.</summary>
    private void InitializeTaskbar()
    {
        ApplyTaskbarRefreshInterval();
        _taskbarTimer.Tick += (_, _) => TaskbarTick();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CurrentPrayerSchedule)) { HookTaskbarSchedule(); RefreshTaskbarBlocks(); }
            else if (e.PropertyName == nameof(WeatherInfo)) RefreshTaskbarBlocks();
        };
        DeviceBatteriesChanged += RefreshTaskbarBlocks;
        DeviceBatterySettingsChanged += RefreshTaskbarBlocks;
        SettingsChanged += _ => { ApplyTaskbarRefreshInterval(); ApplyTaskbarSampler(); RefreshTaskbarBlocks(); };
        HookTaskbarSchedule();
        ApplyTaskbarSampler();
        RefreshTaskbarBlocks();
    }

    private void HookTaskbarSchedule()
    {
        if (ReferenceEquals(_taskbarSchedule, CurrentPrayerSchedule)) return;
        if (_taskbarSchedule != null) _taskbarSchedule.PropertyChanged -= OnTaskbarScheduleChanged;
        _taskbarSchedule = CurrentPrayerSchedule;
        if (_taskbarSchedule != null) _taskbarSchedule.PropertyChanged += OnTaskbarScheduleChanged;
    }

    private void OnTaskbarScheduleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PrayerSchedule.NextPrayer) or nameof(PrayerSchedule.CountdownText))
            RefreshTaskbarBlocks();
    }

    /// <summary>Starts or stops the sampler timer: it runs only while a sampled item is on and the PC is in use (not locked, display on).</summary>
    private void ApplyTaskbarSampler()
    {
        var options = BuildTaskbarOptions();
        bool wanted = options.NeedsSampler && !_taskbarStopped && !_isWorkstationLocked && _displayAwake;
        if (wanted && !_taskbarTimer.IsEnabled)
        {
            _hardwareService.ResetTaskbarSampling();
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
            _taskbarSample = _hardwareService.SampleTaskbar(options.Needs);
            RefreshTaskbarBlocks();
        }
        catch (Exception ex) { AppLog.Warn("Taskbar", ex); }
    }

    private TaskbarSnapshot BuildLiveTaskbarSnapshot(TaskbarOptions options) => new()
    {
        CpuPercent = _taskbarSample.CpuPercent, CpuTempC = _taskbarSample.CpuTempC, CpuTempMeasured = _taskbarSample.CpuTempMeasured,
        GpuPercent = _taskbarSample.GpuPercent, GpuTempC = _taskbarSample.GpuTempC,
        RamPercent = _taskbarSample.RamPercent, DiskPercent = _taskbarSample.DiskPercent,
        NetDownBytesPerSecond = _taskbarSample.NetDownBytesPerSecond, NetUpBytesPerSecond = _taskbarSample.NetUpBytesPerSecond,
        PrayerName = CurrentPrayerSchedule?.NextPrayer?.Name ?? "",
        PrayerTime = CurrentPrayerSchedule?.NextPrayer?.FormattedTime ?? "",
        PrayerRemaining = CurrentPrayerSchedule?.NextPrayerInstant is not null ? CurrentPrayerSchedule.TimeUntilNextPrayer : null,
        WeatherTempC = _weatherInfo?.Temperature,
        WindMetersPerSecond = _weatherInfo?.WindSpeed,
        WeatherGlyph = _weatherInfo?.WeatherIcon ?? "",
        WeatherCondition = _weatherInfo?.Condition ?? "",
        Batteries = options.Batteries
            ? TaskbarDeviceBatteries.Select(r => new TaskbarBattery(Views.Cards.DeviceBatteryRowConverter.Glyph(r.Kind), r.Name, r.Via, r.Percent, r.Charging, r.Stale, r.ReadAtUtc)).ToArray()
            : Array.Empty<TaskbarBattery>(),
    };

    /// <summary>Rebuilds Left and Right blocks from the latest data and raises <see cref="TaskbarBlocksChanged"/> only when either side differs.</summary>
    public void RefreshTaskbarBlocks()
    {
        var options = BuildTaskbarOptions();
        var snapshot = BuildLiveTaskbarSnapshot(options);
        var (left, right) = TaskbarReadouts.BuildSplit(snapshot, options);
        if (left.SequenceEqual(_taskbarLeftBlocks) && right.SequenceEqual(_taskbarRightBlocks)) return;
        _taskbarLeftBlocks = left;
        _taskbarRightBlocks = right;
        _taskbarBlocks = left.Concat(right).ToArray();
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
