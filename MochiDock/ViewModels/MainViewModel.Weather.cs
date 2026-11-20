using MochiDock.Core;
using MochiDock.Models;
using MochiDock.Services;

namespace MochiDock.ViewModels;

/// <summary>
/// "Rain soon": after each weather refresh, a short capsule notice (the timed hardware-notice slot) when rain, snow or a
/// thunderstorm is expected within the hour. <see cref="RainSoonPolicy"/> decides; tapping the notice opens Weather.
/// </summary>
public partial class MainViewModel
{
    private static readonly TimeSpan WeatherPollNormal = TimeSpan.FromMinutes(20), WeatherPollRainExpected = TimeSpan.FromMinutes(10);
    private bool _noticeIsWeather;
    private readonly RainSoonPolicy _rainSoon = new();

    public bool RainSoonNotice
    {
        get => _settings?.RainSoonNotice ?? true;
        set
        {
            if (_settings == null || _settings.RainSoonNotice == value) return;
            UpdateSettings(s => s.RainSoonNotice = value, SettingsApply.None);
            OnPropertyChanged();
        }
    }

    /// <summary>Called on the UI thread after each weather poll.</summary>
    private void CheckRainSoon(WeatherInfo? weather)
    {
        var slots = weather?.Next15 ?? new List<PrecipitationSlot>();
        var now = DateTimeOffset.UtcNow;
        // Poll more often while rain is due in the next two hours, so the notice is not up to 20 minutes late.
        bool rainAhead = slots.Any(s => s.Start > now && RainSoonPolicy.KindOf(s.WeatherCode, s.Millimetres) != RainSoonKind.None);
        var interval = rainAhead ? WeatherPollRainExpected : WeatherPollNormal;
        if (_weatherPollTimer.Interval != interval) _weatherPollTimer.Interval = interval;

        var notice = _rainSoon.Evaluate(now, slots);   // always evaluated, so the policy tracks dry and wet spells even while muted
        if (notice is not { } rain || !RainSoonNotice) return;
        if (_hardwarePresentationSuppressed || _wasGaming || _isWorkstationLocked || !_displayAwake ||
            IsNotificationInMainPill || IsPrayerArrivalVisible || IsPrayerReminderVisible || !ShouldShowNotification("Hardware")) return;
        AppLog.Warn("Weather", $"Rain soon notice: {rain.Text}.");
        _noticeIsAi = false; _noticeIsAgenda = false; _noticeIsGame = false; _noticeIsDevice = false; _noticeIsWeather = true;
        _hardwareNoticeText = rain.Text;
        _hardwareNoticeUntil = DateTimeOffset.UtcNow.AddSeconds(8);
        _hardwareNoticeTimer.Stop(); _hardwareNoticeTimer.Start();
        NotifyHardwareNotice();
    }
}
