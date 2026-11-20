using System.Windows.Threading;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.ViewModels;

/// <summary>
/// The 24-hour Stats history and the sustained-heat alert. A 15-second timer records one sample (the Stats poll's latest reading when it
/// ran in the last 15 seconds, otherwise <see cref="HardwareMonitorService.GetLightSample"/>), folded into one point per minute by
/// <see cref="StatsHistory"/>. Nothing is recorded while the PC is locked or the screen is off, or with the setting off.
/// </summary>
public partial class MainViewModel
{
    public static readonly TimeSpan HistorySampleEvery = TimeSpan.FromSeconds(15);

    private StatsHistoryService? _statsHistory;
    private DispatcherTimer? _historyTimer;
    private DateTimeOffset _lastFullSampleAt;
    private readonly SustainedHeatPolicy _sustainedHeat = new();
    private bool _mascotSustainedHot;

    public StatsHistoryService? StatsHistory => _statsHistory;

    public void ConfigureStatsHistory(StatsHistoryService history)
    {
        _statsHistory = history;
        _historyTimer ??= new DispatcherTimer { Interval = HistorySampleEvery };
        _historyTimer.Tick -= HistoryTick;
        _historyTimer.Tick += HistoryTick;
        _historyTimer.Start();
        OnPropertyChanged(nameof(StatsHistory));
    }

    public void StopStatsHistory() => _historyTimer?.Stop();

    public bool RecordStatsHistory
    {
        get => _settings?.RecordStatsHistory ?? true;
        set
        {
            if (_settings == null || _settings.RecordStatsHistory == value) return;
            UpdateSettings(s => s.RecordStatsHistory = value, SettingsApply.None);
            OnPropertyChanged();
        }
    }

    /// <summary>Called by the Stats poll after each full reading, so the history can reuse it instead of reading again.</summary>
    private void NoteFullSample() => _lastFullSampleAt = DateTimeOffset.UtcNow;

    private void HistoryTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (_isWorkstationLocked || !_displayAwake)
        {
            _sustainedHeat.Reset();
            SetSustainedHot(false);
            return;
        }
        bool record = _settings.RecordStatsHistory && _statsHistory != null;
        bool alert = EnableHardwareAlerts;
        if (!record && !alert) return;

        var sample = now - _lastFullSampleAt < HistorySampleEvery && _hardwareStats is { } s
            ? new HistorySample(now, s.CpuUsageReading?.Value ?? s.CpuUsage, s.CpuTemperatureReading?.Value, s.GpuUsageReading?.Value,
                s.GpuTemperatureReading?.Value, s.RamUsagePercent > 0 ? s.RamUsagePercent : null)
            : _hardwareService.GetLightSample(now);

        if (record) _statsHistory!.Add(sample);
        if (alert) CheckSustainedHeat(sample, now);
        else SetSustainedHot(false);
        RefreshHistoryCharts();
    }

    private void CheckSustainedHeat(HistorySample sample, DateTimeOffset now)
    {
        double threshold = Math.Clamp(_settings.SustainedHeatThreshold, 50, 105);
        var hold = TimeSpan.FromMinutes(Math.Clamp(_settings.SustainedHeatMinutes, 1, 120));
        string? fired = null;
        if (_sustainedHeat.Evaluate("GPU", sample.GpuTemp, threshold, hold, now)) fired = "GPU";
        if (_sustainedHeat.Evaluate("CPU", sample.CpuTemp, threshold, hold, now)) fired ??= "CPU";
        SetSustainedHot(_sustainedHeat.IsSustained("GPU", hold, now) || _sustainedHeat.IsSustained("CPU", hold, now));
        if (fired == null) return;

        AppLog.Warn("Hardware", $"Sustained heat: {fired} at {threshold:0} °C or more for {hold.TotalMinutes:0} min.");
        if (_hardwarePresentationSuppressed || _wasGaming || IsNotificationInMainPill || IsPrayerReminderVisible || !ShouldShowNotification("Hardware")) return;
        _noticeIsAi = false; _noticeIsAgenda = false; _noticeIsGame = false; _noticeIsDevice = false; _noticeIsWeather = false;
        _noticeHeatModule = fired;
        _hardwareNoticeText = $"{fired} at {threshold:0} °C+ for {hold.TotalMinutes:0} min";
        _hardwareNoticeUntil = DateTimeOffset.UtcNow.AddSeconds(8);
        _hardwareNoticeTimer.Stop(); _hardwareNoticeTimer.Start();
        NotifyHardwareNotice();
    }

    // The module the last sustained-heat notice was about: tapping it opens that Stats page in the 24 h view.
    private string? _noticeHeatModule;

    // ---- Charts: "2 min | 24 h". The 24 h lines are frozen geometries in the 2-minute charts' 220 x 100 point space.
    public static readonly TimeSpan ChartSpan24h = TimeSpan.FromHours(24);

    public bool IsStatsChart24h => string.Equals(_settings?.StatsChartSpan, "24h", StringComparison.OrdinalIgnoreCase);
    public string StatsChartSpanLabel => IsStatsChart24h ? "24 H" : "2 MIN";
    public string StatsChartSpanTooltip => IsStatsChart24h
        ? "Showing the last 24 hours (one point a minute). Click for the live 2-minute view."
        : "Showing the last 2 minutes. Click for the last 24 hours.";

    public string StatsChartSpan
    {
        get => IsStatsChart24h ? "24h" : "2m";
        set
        {
            string span = value == "24h" ? "24h" : "2m";
            if (_settings == null || _settings.StatsChartSpan == span) return;
            UpdateSettings(s => s.StatsChartSpan = span, SettingsApply.None);
            OnPropertyChanged(nameof(IsStatsChart24h));
            OnPropertyChanged(nameof(StatsChartSpanLabel));
            OnPropertyChanged(nameof(StatsChartSpanTooltip));
            RefreshHistoryCharts(force: true);
        }
    }

    public void ToggleStatsChartSpan() => StatsChartSpan = IsStatsChart24h ? "2m" : "24h";

    public System.Windows.Media.Geometry? CpuLoad24Geometry { get; private set; }
    public System.Windows.Media.Geometry? CpuTemp24Geometry { get; private set; }
    public System.Windows.Media.Geometry? GpuLoad24Geometry { get; private set; }
    public System.Windows.Media.Geometry? GpuTemp24Geometry { get; private set; }
    public System.Windows.Media.Geometry? Ram24Geometry { get; private set; }
    public string CpuPeak24Text { get; private set; } = "";
    public string GpuPeak24Text { get; private set; } = "";
    public string RamPeak24Text { get; private set; } = "";
    /// <summary>False until the history has a few minutes: the 24 h chart then says it is still collecting.</summary>
    public bool HasStatsHistory { get; private set; }

    private DateTimeOffset _historyChartsDrawnAt;

    /// <summary>Rebuilds the 24 h lines while they are on screen (Stats open in 24 h mode), at most once a minute unless forced.</summary>
    private void RefreshHistoryCharts(bool force = false)
    {
        if (_statsHistory == null || !IsStatsChart24h || !_isExpanded || _activeCardType != IslandCardType.Hardware) return;
        var now = DateTimeOffset.UtcNow;
        if (!force && now - _historyChartsDrawnAt < TimeSpan.FromMinutes(1)) return;
        _historyChartsDrawnAt = now;
        _statsHistory.Read(h =>
        {
            CpuLoad24Geometry = Line(h.Series(HistoryMetric.CpuLoad, now, ChartSpan24h, 220, 100, 100));
            CpuTemp24Geometry = Line(h.Series(HistoryMetric.CpuTemp, now, ChartSpan24h, 220, 100, 100));
            GpuLoad24Geometry = Line(h.Series(HistoryMetric.GpuLoad, now, ChartSpan24h, 220, 100, 100));
            GpuTemp24Geometry = Line(h.Series(HistoryMetric.GpuTemp, now, ChartSpan24h, 220, 100, 100));
            Ram24Geometry = Line(h.Series(HistoryMetric.RamPercent, now, ChartSpan24h, 220, 100, 100));
            CpuPeak24Text = PeakText(h.Peak(HistoryMetric.CpuTemp, now, ChartSpan24h), "°C") ?? PeakText(h.Peak(HistoryMetric.CpuLoad, now, ChartSpan24h), "%") ?? "";
            GpuPeak24Text = PeakText(h.Peak(HistoryMetric.GpuTemp, now, ChartSpan24h), "°C") ?? PeakText(h.Peak(HistoryMetric.GpuLoad, now, ChartSpan24h), "%") ?? "";
            RamPeak24Text = PeakText(h.Peak(HistoryMetric.RamPercent, now, ChartSpan24h), "%") ?? "";
            HasStatsHistory = h.Count >= 3;
            return 0;
        });
        foreach (var name in new[] { nameof(CpuLoad24Geometry), nameof(CpuTemp24Geometry), nameof(GpuLoad24Geometry), nameof(GpuTemp24Geometry),
                     nameof(Ram24Geometry), nameof(CpuPeak24Text), nameof(GpuPeak24Text), nameof(RamPeak24Text), nameof(HasStatsHistory) })
            OnPropertyChanged(name);
    }

    private static string? PeakText((double Value, DateTimeOffset At)? peak, string unit) =>
        peak is { } p ? $"Peak {p.Value:0}{(unit == "%" ? "%" : " " + unit)} at {p.At.ToLocalTime():HH:mm}" : null;

    private static System.Windows.Media.Geometry? Line(IReadOnlyList<IReadOnlyList<(double X, double Y)>> segments)
    {
        if (segments.Count == 0) return null;
        var geometry = new System.Windows.Media.StreamGeometry();
        using (var context = geometry.Open())
        {
            foreach (var segment in segments)
            {
                context.BeginFigure(new System.Windows.Point(segment[0].X, segment[0].Y), false, false);
                if (segment.Count == 1) context.LineTo(new System.Windows.Point(segment[0].X + 1, segment[0].Y), true, false);   // a lone minute
                else context.PolyLineTo(segment.Skip(1).Select(p => new System.Windows.Point(p.X, p.Y)).ToList(), true, true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private void SetSustainedHot(bool hot)
    {
        if (_mascotSustainedHot == hot) return;
        _mascotSustainedHot = hot;
        RefreshMascotMood();
    }
}
