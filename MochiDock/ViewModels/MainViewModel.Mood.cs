using System;
using System.Linq;
using System.Windows.Threading;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.ViewModels;

public partial class MainViewModel
{
    private DispatcherTimer? _happyTimer;

    // What the user is looking at or doing (the Stats Battery page, FX, Weather, a running Pomodoro): see MascotTriggers.ActivityFace.
    private MascotMood? _mascotActivity;

    /// <summary>Works out the activity face from the open card and the Pomodoro; cheap, so it runs on every clock tick and on navigation.</summary>
    private void UpdateActivityFace()
    {
        var view = _activeCardType switch
        {
            IslandCardType.Hardware when IsBatteryModuleActive => MascotTriggers.DockView.StatsBattery,
            IslandCardType.FxRates => MascotTriggers.DockView.Fx,
            IslandCardType.Weather => MascotTriggers.DockView.Weather,
            _ => MascotTriggers.DockView.Other,
        };
        var state = _pomodoroService?.State;
        var face = MascotTriggers.ActivityFace(state == PomodoroState.Working, _isExpanded, view,
            view == MascotTriggers.DockView.Fx ? Fx?.SelectedChangePercent() : null, _weatherInfo?.IconKind, DateTime.Now, state == PomodoroState.Break);
        if (face == _mascotActivity) return;
        _mascotActivity = face;
        RefreshMascotMood();
    }

    // Clicking Mochi many times in a short time makes him dizzy (see PettingTracker): one count for every Mochi in the app.
    private readonly PettingTracker _petting = new();
    private DispatcherTimer? _pettedTimer;
    private bool _mascotPetted;

    private void OnMochiPetted()
    {
        var now = DateTime.Now;
        bool dizzy = _petting.Register(now);
        if (dizzy != _mascotPetted)
        {
            _mascotPetted = dizzy;
            RefreshMascotMood();
        }
        if (!dizzy || _petting.DizzyUntil is not { } until) return;

        // One one-shot timer, moved to the new end each time another click extends the dizziness.
        _pettedTimer ??= new DispatcherTimer();
        _pettedTimer.Tick -= EndPettedDizziness;
        _pettedTimer.Tick += EndPettedDizziness;
        _pettedTimer.Stop();
        _pettedTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, (until - now).TotalMilliseconds + 20));
        _pettedTimer.Start();
    }

    private void EndPettedDizziness(object? sender, EventArgs e)
    {
        _pettedTimer?.Stop();
        if (_petting.IsDizzy(DateTime.Now)) return;
        if (!_mascotPetted) return;
        _mascotPetted = false;
        RefreshMascotMood();
    }

    // While a game runs Mochi shows one of three gaming faces: Focused (serious), Worried (the frame rate fell or the ping is high) or
    // BreakTime (the break reminder is showing). Only measured readings feed the worried face.
    private readonly GameTroubleTracker _gameTrouble = new();
    private bool _mascotGameTrouble;

    private void RefreshGamingFace(bool isGaming)
    {
        if (!isGaming || _baseMood is not (MascotMood.Focused or MascotMood.Worried or MascotMood.BreakTime)) return;
        var want = _breakNoticeShown ? MascotMood.BreakTime : _mascotGameTrouble ? MascotMood.Worried : MascotMood.Focused;
        if (_baseMood != want) MascotMood = want;
    }

    // Feeds Mochi's hot and dizzy faces from the readings the hardware poller already takes (no extra sampling): only measured,
    // fresh values count, like the hardware alerts.
    private void UpdateMascotFromHardware(HardwareStats stats)
    {
        var now = DateTimeOffset.UtcNow;
        _mascotGpuHot = MascotTriggers.GpuHot(stats.GpuTemperatureReading, _settings.TemperatureAlertThreshold, _mascotGpuHot, now);
        _mascotOverloaded = _mascotLoadTracker.Update(DateTime.Now, MascotTriggers.MeasuredLoad(stats.CpuUsageReading, now), stats.RamUsagePercent);
        _mascotGameTrouble = _wasGaming && _gameTrouble.Update(DateTime.Now, stats.CurrentFps, stats.NetworkPingMs);
        if (!_wasGaming) _gameTrouble.Reset();
        RefreshGamingFace(_wasGaming);
        RefreshMascotMood();
    }

    /// <summary>Mochi is angry while an open task is 10 to 90 minutes overdue and nobody acknowledged it (needs overdue reminders on).</summary>
    private void UpdateOverdueMood()
    {
        bool angry = _settings.EnableAgendaReminders && _settings.RemindOverdueTasks &&
            MascotTriggers.UnacknowledgedOverdue(Tasks.Select(t => new ReminderTask(t.Id, t.Title, t.DueTime, t.IsCompleted)).ToList(), DateTime.Now, _acknowledgedReminders);
        if (angry == _mascotOverdue) return;
        _mascotOverdue = angry;
        RefreshMascotMood();
    }

    /// <summary>A few seconds of the happy face after a task is ticked off, unless Mochi is busy with something that matters more.</summary>
    private void ShowHappyMoment()
    {
        if (_baseMood is not (MascotMood.Idle or MascotMood.Waiting or MascotMood.Bored or MascotMood.Music)) return;
        MascotMood = MascotMood.Happy;
        _happyTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _happyTimer.Tick -= EndHappyMoment;
        _happyTimer.Tick += EndHappyMoment;
        _happyTimer.Stop();
        _happyTimer.Start();
    }

    // Lonely: nobody has touched the keyboard or mouse for a long while and nothing is going on. Checked every few seconds.
    private DateTime _lastLonelyCheckUtc;

    private void UpdateLonelyMood(bool isGaming)
    {
        var now = DateTime.UtcNow;
        if (now - _lastLonelyCheckUtc < TimeSpan.FromSeconds(5)) return;
        _lastLonelyCheckUtc = now;
        bool lonely = _settings.ExpressiveMochi && MascotTriggers.Lonely(UserIdleService.IdleTime(), CurrentMedia.IsPlaying, isGaming, _isWorkstationLocked, _displayAwake);
        if (lonely && _baseMood is MascotMood.Idle or MascotMood.Waiting) MascotMood = MascotMood.Bored;
        else if (!lonely && _baseMood == MascotMood.Bored) MascotMood = _wasGaming ? MascotMood.Focused : (CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle);
    }

    private DispatcherTimer? _momentTimer;
    private MascotMood _momentMood;

    /// <summary>
    /// A short face for an event (surprised, cheering, praying): shown for <paramref name="duration"/>, then Mochi goes back to his
    /// resting face. Not while a game is being played, the PC is locked or the screen is off.
    /// </summary>
    private void ShowMoment(MascotMood mood, TimeSpan duration)
    {
        if (_wasGaming || _isWorkstationLocked || !_displayAwake) return;
        _momentMood = mood;
        MascotMood = mood;
        _momentTimer ??= new DispatcherTimer();
        _momentTimer.Tick -= EndMoment;
        _momentTimer.Tick += EndMoment;
        _momentTimer.Stop();
        _momentTimer.Interval = duration;
        _momentTimer.Start();
    }

    private void EndMoment(object? sender, EventArgs e)
    {
        _momentTimer?.Stop();
        if (_baseMood == _momentMood) MascotMood = _wasGaming ? MascotMood.Focused : (CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle);
    }

    /// <summary>A few yawning seconds after a long absence, or when the app starts in the morning.</summary>
    private void ShowWakingMoment() => ShowMoment(MascotMood.Waking, TimeSpan.FromSeconds(6));

    /// <summary>Something arrived (a download finished, a file came from the phone): a short surprised face.</summary>
    public void ShowSurprisedMoment() => ShowMoment(MascotMood.Surprised, TimeSpan.FromSeconds(3));

    private DispatcherTimer? _resultTimer;

    /// <summary>
    /// A few seconds of the victory or defeat face after a match result is known (League of Legends at the end of the session, PUBG
    /// when its lookup finds the match). Not while a game is being played, the PC is locked or the screen is off.
    /// </summary>
    private void ShowMatchResultMoment(GameMatchSummary? match)
    {
        if (MascotTriggers.MatchMood(match) is not { } mood) return;
        if (_wasGaming || _isWorkstationLocked || !_displayAwake) return;
        MascotMood = mood;
        _resultTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _resultTimer.Tick -= EndResultMoment;
        _resultTimer.Tick += EndResultMoment;
        _resultTimer.Stop();
        _resultTimer.Start();
    }

    private void EndResultMoment(object? sender, EventArgs e)
    {
        _resultTimer?.Stop();
        if (_baseMood is MascotMood.Victory or MascotMood.Defeat) MascotMood = _wasGaming ? MascotMood.Focused : (CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle);
    }

    private void EndHappyMoment(object? sender, EventArgs e)
    {
        _happyTimer?.Stop();
        if (_baseMood == MascotMood.Happy) MascotMood = _wasGaming ? MascotMood.Focused : (CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle);
    }
}
