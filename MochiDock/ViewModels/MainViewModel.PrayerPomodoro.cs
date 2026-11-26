using System.Windows.Threading;
using MochiDock.Models;
using MochiDock.Services;
using MascotMood = MochiDock.Core.MascotMood;

namespace MochiDock.ViewModels;

public partial class MainViewModel
{
    private PrayerSchedule? _currentPrayerSchedule;
    private readonly DispatcherTimer _prayerTimer;

    // Pomodoro Widget
    public bool IsPomodoroInMainPill =>
        _pomodoroService != null && _pomodoroService.IsActive &&
        !HasActiveMedia && !IsStatsShownInCapsule && !IsNotificationInMainPill && !IsPrayerReminderVisible && !IsPrayerArrivalVisible;

    public bool IsBothPomodoroAndWeatherInMainPill => IsPomodoroInMainPill && IsWeatherInMainPill;

    public string PomodoroPillText => _pomodoroService != null && _pomodoroService.IsActive
        ? $"{_pomodoroService.FormattedTime} · {(_pomodoroService.State == PomodoroState.Break ? "Break" : (_pomodoroService.State == PomodoroState.Paused ? "Paused" : "Focus"))}"
        : "";

    public string PomodoroTimeDisplay => _pomodoroService?.FormattedTime ?? "25:00";
    public double PomodoroProgress
    {
        get => _pomodoroService?.ProgressPercentage ?? 0;
        set { }
    }
    public string PomodoroStateText => _pomodoroService?.State switch
    {
        PomodoroState.Working => "FOCUSING",
        PomodoroState.Break => "ON BREAK",
        PomodoroState.Paused => "PAUSED",
        _ => "POMODORO"
    };
    public string PomodoroModeLabel => _pomodoroService?.State switch
    {
        PomodoroState.Working => "Stay focused! You got this.",
        PomodoroState.Break => "Take a deep breath and stretch.",
        PomodoroState.Paused => "Timer paused.",
        _ => "25m Focus / 5m Break"
    };
    public string PomodoroPlayPauseIcon => (_pomodoroService?.IsRunning ?? false) ? "⏸" : "▶";
    public string PomodoroPlayPauseLabel => (_pomodoroService?.IsRunning ?? false) ? "Pause" : "Start";

    public PrayerSchedule? CurrentPrayerSchedule
    {
        get => _currentPrayerSchedule;
        set { _currentPrayerSchedule = value; OnPropertyChanged(); OnPropertyChanged(nameof(PrayerPillCountdownText)); OnPropertyChanged(nameof(IsPrayerInMainPill)); NotifyPrayerPresentation(); }
    }

    public bool EnablePrayerTimes
    {
        get => _settings?.EnablePrayerTimes ?? true;
        set
        {
            if (_settings != null && _settings.EnablePrayerTimes != value)
            {
                if (!value) { PrayerAudio.Stop(); _prayerCancellation?.Cancel(); } else { _ = UpdatePrayerLocationAndScheduleAsync(); }
                UpdateSettings(x => x.EnablePrayerTimes = value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPrayerInMainPill));
                NotifyPrayerPresentation();
                if (!value && ActiveCardType == IslandCardType.Prayer) ActiveCardType = IslandCardType.Media;
            }
        }
    }

    public bool EnablePrayerPreNotification
    {
        get => _settings?.EnablePrayerPreNotification ?? true;
        set
        {
            if (_settings != null && _settings.EnablePrayerPreNotification != value)
            {
                UpdateSettings(x => x.EnablePrayerPreNotification = value);
                OnPropertyChanged();
                NotifyPrayerPresentation();
            }
        }
    }

    public string PrayerSoundMode
    {
        get => _settings?.PrayerSoundMode ?? "Tone";
        set
        {
            string normalized = MochiDock.Core.PrayerTime.NormalizeSound(value);
            if (_settings != null && !string.Equals(_settings.PrayerSoundMode, normalized, StringComparison.Ordinal))
            {
                PrayerAudio.Stop();
                UpdateSettings(x => x.PrayerSoundMode = normalized);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPrayerSoundTone));
                OnPropertyChanged(nameof(IsPrayerSoundSilent));
            }
        }
    }

    public bool IsPrayerSoundTone => PrayerSoundMode == "Tone";
    public bool IsPrayerSoundSilent => PrayerSoundMode == "Silent";

    private readonly MochiDock.Core.PrayerCountdownState _prayerCountdown = new();
    public bool IsPrayerReminderVisible => _settings.EnablePrayerTimes && _settings.EnablePrayerPreNotification && _settings.ShowPrayerInCapsule && !_isWorkstationLocked && !IsNotificationInMainPill && ShouldShowNotification("Prayer") && CurrentPrayerSchedule?.NextPrayerInstant is { } at && _prayerCountdown.Evaluate($"{CurrentPrayerSchedule.Latitude}|{CurrentPrayerSchedule.Longitude}|{CurrentPrayerSchedule.NextPrayer?.Name}|{at:O}", at, CurrentPrayerSchedule.NowUtc);
    public string PrayerReminderText => CurrentPrayerSchedule?.NextPrayerInstant is { } at ? $"{CurrentPrayerSchedule.NextPrayer?.Name} {MochiDock.Core.PrayerCountdownState.Minutes(at, CurrentPrayerSchedule.NowUtc)} min" : "";
    public void AcknowledgePrayerCountdown() { _prayerCountdown.Acknowledge(); NotifyPrayerPresentation(); }

    private DateTimeOffset _prayerArrivalUntil;
    private readonly DispatcherTimer _prayerArrivalTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private string _prayerArrivalTitle = "";
    private string _prayerArrivalSubtitle = "";

    public bool IsPrayerArrivalVisible =>
        _settings.EnablePrayerTimes && _settings.ShowPrayerInCapsule &&
        !_isWorkstationLocked && _displayAwake && !_wasGaming &&
        !IsNotificationInMainPill && ShouldShowNotification("Prayer") &&
        DateTimeOffset.UtcNow < _prayerArrivalUntil;

    public string PrayerArrivalTitle => _prayerArrivalTitle;
    public string PrayerArrivalSubtitle => _prayerArrivalSubtitle;

    public void ShowPrayerArrival(string prayerName, DateTimeOffset prayerAt, string city)
    {
        string displayName = prayerName.Equals("Asr", StringComparison.OrdinalIgnoreCase) ? "Ashar" : prayerName;
        _prayerArrivalTitle = $"It's time for {displayName}";
        string timePart = prayerAt.ToString("HH:mm");
        if (CurrentPrayerSchedule != null && !string.IsNullOrWhiteSpace(CurrentPrayerSchedule.TimeZoneId))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(CurrentPrayerSchedule.TimeZoneId);
                timePart = TimeZoneInfo.ConvertTime(prayerAt, zone).ToString("HH:mm");
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
        }
        string cityPart = !string.IsNullOrWhiteSpace(city) ? city : (CurrentPrayerSchedule?.ResolvedLocation ?? "");
        _prayerArrivalSubtitle = !string.IsNullOrWhiteSpace(cityPart) ? $"{timePart} · {cityPart}" : timePart;
        _prayerArrivalUntil = DateTimeOffset.UtcNow.AddSeconds(60);
        _prayerArrivalTimer?.Stop();
        _prayerArrivalTimer?.Start();
        NotifyPrayerArrival();
        ShowMoment(MascotMood.Praying, TimeSpan.FromSeconds(60));
    }

    public void DismissPrayerArrival()
    {
        _prayerArrivalUntil = default;
        _prayerArrivalTimer?.Stop();
        NotifyPrayerArrival();
    }

    private void NotifyPrayerArrival()
    {
        OnPropertyChanged(nameof(IsPrayerArrivalVisible));
        OnPropertyChanged(nameof(PrayerArrivalTitle));
        OnPropertyChanged(nameof(PrayerArrivalSubtitle));
        NotifyPrayerPresentation();
    }

    private void NotifyPrayerPresentation()
    {
        OnPropertyChanged(nameof(IsPrayerReminderVisible));
        OnPropertyChanged(nameof(PrayerReminderText));
        OnPropertyChanged(nameof(IsWeatherInPrayerReminder));
        OnPropertyChanged(nameof(IsWeatherInMainPill));
        OnPropertyChanged(nameof(IsPomodoroInMainPill));
        OnPropertyChanged(nameof(IsBothPomodoroAndWeatherInMainPill));
        OnPropertyChanged(nameof(HasCompactLyricRomaji));
        OnPropertyChanged(nameof(IsAudioWaveformVisible));
        OnPropertyChanged(nameof(CompactCapsuleText));
        OnPropertyChanged(nameof(IsCompactMarqueeEnabled));
        OnPropertyChanged(nameof(IsCompactTextVisible));
    }
    public bool IsPrayerInMainPill =>
        (_settings?.ShowPrayerInCapsule ?? true) &&
        _currentPrayerSchedule?.NextPrayer != null &&
        !HasActiveMedia && !IsStatsShownInCapsule && !IsNotificationInMainPill && !IsPomodoroInMainPill;

    public string PrayerPillCountdownText =>
        _currentPrayerSchedule?.NextPrayer != null ? $"🕌 {_currentPrayerSchedule.NextPrayer.Name} in {_currentPrayerSchedule.CountdownText}" : "";

    private long _prayerLocationGeneration;
    public async Task UpdatePrayerLocationAndScheduleAsync()
    {
        long generation = ++_prayerLocationGeneration;
        _prayerCancellation?.Cancel(); ++_prayerGeneration; CurrentPrayerSchedule = null; PrayerAudio.Stop();
        string city = _settings.WeatherCity;
        try
        {
            var location = await _weatherService.ResolveLocationAsync(city);
            if (generation != _prayerLocationGeneration || city != _settings.WeatherCity) return;
            if (!location.Success)
            {
                if (!await _prayerTimesService.RestoreCachedLocationAsync(city)) throw new InvalidOperationException("Cannot resolve prayer location. Connect once to resolve this city.");
                if (generation != _prayerLocationGeneration || city != _settings.WeatherCity) return;
            }
            else _prayerTimesService.SetCoordinates(location.Latitude, location.Longitude, city, location.ShortName);
            await UpdatePrayerScheduleAsync();
        }
        catch (Exception ex) { if (generation == _prayerLocationGeneration) { CurrentPrayerSchedule = null; PrayerError = ex.Message; OnPropertyChanged(nameof(PrayerError)); } }
    }
    private long _prayerGeneration;
    private CancellationTokenSource? _prayerCancellation;
    private readonly MochiDock.Core.PrayerReminderGate _prayerReminders = new();
    private DateTimeOffset _lastPrayerAttempt;
    public string PrayerError { get; private set; } = "";
    public async Task UpdatePrayerScheduleAsync()
    {
        if (!_settings.EnablePrayerTimes) return;
        long generation = ++_prayerGeneration;
        _prayerCancellation?.Cancel(); _prayerCancellation?.Dispose(); _prayerCancellation = new();
        var token = _prayerCancellation.Token;
        _lastPrayerAttempt = DateTimeOffset.UtcNow;
        try
        {
            var schedule = await _prayerTimesService.FetchOrCalculateScheduleAsync(DateTime.UtcNow.Date, _settings.PrayerCalculationMethod, token);
            if (generation != _prayerGeneration || token.IsCancellationRequested) return;
            CurrentPrayerSchedule = schedule; PrayerError = "";
            _prayerReminders.Reset(DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _prayerGeneration) { CurrentPrayerSchedule = null; PrayerError = ex.Message; } }
        OnPropertyChanged(nameof(PrayerError));
    }
    public void UpdatePrayerSchedule()
    {
        if (!_settings.EnablePrayerTimes) { PrayerAudio.Stop(); return; }
        var now = DateTimeOffset.UtcNow;
        var schedule = CurrentPrayerSchedule;
        if (schedule == null) { if ((now - _lastPrayerAttempt).TotalSeconds >= 30) _ = UpdatePrayerScheduleAsync(); return; }
        schedule.UpdateCountdown();
        NotifyPrayerPresentation();
        var date = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId)).Date;
        if (date != schedule.Date.Date) { if ((now - _lastPrayerAttempt).TotalSeconds >= 30) _ = UpdatePrayerScheduleAsync(); return; }
        if (_isWorkstationLocked) { _prayerReminders.Reset(now); DismissPrayerArrival(); return; }
        if (_prayerArrivalUntil != default && now >= _prayerArrivalUntil) { DismissPrayerArrival(); }
        foreach (var prayer in schedule.Instants())
        {
            string key = $"{schedule.Latitude}|{schedule.Longitude}|{prayer.Name}|{prayer.At:O}";
            if (_settings.EnablePrayerPreNotification && _prayerReminders.Due(key + "-pre", prayer.At.AddMinutes(-10), now) && ShouldShowNotification("Prayer"))
            { OnPropertyChanged(nameof(IsPrayerReminderVisible)); }
            if (_prayerReminders.Due(key, prayer.At, now))
            {
                _ = PlayPrayerEventAsync();
                if (_settings.EnablePrayerTimes && _settings.ShowPrayerInCapsule && ShouldShowNotification("Prayer"))
                {
                    ShowPrayerArrival(prayer.Name, prayer.At, schedule.City);
                }
            }
        }
        _prayerReminders.Advance(now);
    }
    private async Task PlayPrayerEventAsync()
    {
        try { await PrayerAudio.PlayAsync(PrayerSoundMode); }
        catch (Exception ex) { PrayerError = ex.Message; OnPropertyChanged(nameof(PrayerError)); }
    }
    /// <summary>Plays the prayer chime once for the current sound mode (silent plays nothing); used when the user picks Tone.</summary>
    public void PreviewPrayerSound() => _ = PlayPrayerEventAsync();
    public void TogglePomodoro() => _pomodoroService.TogglePlayPause();
    public void ResetPomodoro() => _pomodoroService.Reset();
    public void StartPomodoro(int minutes) => _pomodoroService.StartWork(minutes);
    public void StartPomodoroBreak(int minutes) => _pomodoroService.StartBreak(minutes);
}
