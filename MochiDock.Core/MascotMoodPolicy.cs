using System;

namespace MochiDock.Core;

/// <summary>The faces Mochi can wear. The first group is state the rest of the app sets; the second group is chosen by <see cref="MascotMoodPolicy"/>.</summary>
public enum MascotMood
{
    Idle, Waiting, Bored, Music, Alert, Sleeping, Focused, Happy,
    Hot, Angry, Freezing, Dizzy, Drained,
    Victory, Defeat,
    // Gaming sub-faces: the game is running (like Focused) but going badly, or the break reminder is showing.
    Worried, BreakTime,
    // Events: a new message (instead of Alert), a short surprise, a short cheer, and the prayer time.
    Excited, Surprised, Cheering, Praying,
    // Ambient: just woke up (a short moment). Bored is the lonely face: nobody has touched the PC for a long while.
    Waking,
    // Activity faces: what the user is looking at or doing. Electric on the Stats Battery page, Money/MoneyDown on FX (rate up or flat /
    // down), Sunny/Rainy/Snowy/Cloudy on Weather (the current sky), Studying while a Pomodoro focus session runs.
    Electric, Money, MoneyDown, Sunny, Rainy, Snowy, Cloudy, Studying,
}

/// <summary>What the weather says about Mochi's comfort.</summary>
public enum MascotWeather { Mild, Hot, Cold }

/// <summary>Everything the policy needs: the mood the app asked for and the ambient conditions.</summary>
public sealed record MascotInputs(
    MascotMood Base, bool Expressive, bool OverdueUnacknowledged, bool GpuHot, bool Overloaded, bool BatteryLow, MascotWeather Weather,
    bool PettedTooMuch = false, bool ScreenOff = false, MascotMood? Activity = null);

/// <summary>
/// One rule for which face wins. Priority, highest first: the gaming faces (Focused, Worried, BreakTime) always show, and so does Sleeping (locked or inactive) unless a lasting ambient face applies (Angry, Hot, Dizzy, Drained), which wakes Mochi, and except while the PC is locked or the screen is off; then Dizzy from
/// being petted too much (a click spree), then Alert or Excited (a notification); then the short moments Victory, Defeat, Surprised, Cheering and Praying (a few seconds after an event, so they beat the lasting faces below); then Angry (an overdue item nobody acknowledged), Hot (the GPU is at its alert temperature), Dizzy (CPU or RAM
/// maxed out for a while), Drained (battery low and unplugged); then Music; then the weather (hot or freezing) while Mochi is only
/// idling; everything else keeps the requested mood (Happy, Idle, Waiting, Bored). The activity face (Electric, Money, a sky face, Studying) comes
/// after the lasting faces and before Music and the weather: it is what the user is looking at, but a warning still matters more.
/// With "expressive" off the requested mood is used as is.
/// </summary>
public static class MascotMoodPolicy
{
    public static MascotMood Choose(MascotInputs i)
    {
        if (!i.Expressive) return i.Base;
        // Asleep from inactivity only means nobody touched the dock: the lasting ambient faces (Angry, Hot, Dizzy, Drained) wake Mochi up.
        // A locked PC or a dark screen (ScreenOff) always sleeps.
        if (i.Base == MascotMood.Sleeping)
        {
            if (i.ScreenOff) return MascotMood.Sleeping;
            if (i.OverdueUnacknowledged) return MascotMood.Angry;
            if (i.GpuHot) return MascotMood.Hot;
            if (i.Overloaded) return MascotMood.Dizzy;
            if (i.BatteryLow) return MascotMood.Drained;
            return MascotMood.Sleeping;
        }
        if (i.Base is MascotMood.Sleeping or MascotMood.Focused or MascotMood.Worried or MascotMood.BreakTime) return i.Base;
        if (i.PettedTooMuch) return MascotMood.Dizzy;   // the click is the user's own: the feedback is immediate and beats everything but sleeping and gaming
        if (i.Base is MascotMood.Alert or MascotMood.Excited) return i.Base;
        if (i.Base is MascotMood.Victory or MascotMood.Defeat or MascotMood.Surprised or MascotMood.Cheering or MascotMood.Praying or MascotMood.Waking) return i.Base;   // a short moment after a match: it must not wait behind a lasting face
        if (i.OverdueUnacknowledged) return MascotMood.Angry;
        if (i.GpuHot) return MascotMood.Hot;
        if (i.Overloaded) return MascotMood.Dizzy;
        if (i.BatteryLow) return MascotMood.Drained;
        if (i.Activity is { } activity && i.Base is MascotMood.Idle or MascotMood.Waiting or MascotMood.Bored or MascotMood.Music) return activity;   // a happy moment (task done) still shows
        if (i.Base == MascotMood.Music) return MascotMood.Music;
        if (i.Base is MascotMood.Idle or MascotMood.Waiting or MascotMood.Bored)
        {
            if (i.Weather == MascotWeather.Hot) return MascotMood.Hot;
            if (i.Weather == MascotWeather.Cold) return MascotMood.Freezing;
        }
        return i.Base;
    }
}

/// <summary>Turns the outside temperature into hot, cold or mild with a gap, so the face does not flicker around a threshold.</summary>
public sealed class MascotWeatherTracker
{
    public const double HotFrom = 33, HotUntil = 31, ColdFrom = 3, ColdUntil = 5;
    private MascotWeather _state = MascotWeather.Mild;

    public MascotWeather State => _state;

    /// <summary>Feeds a temperature in degrees Celsius (null when the weather is unknown, which counts as mild).</summary>
    public MascotWeather Update(double? celsius)
    {
        if (celsius is not { } t || !double.IsFinite(t)) return _state = MascotWeather.Mild;
        _state = _state switch
        {
            MascotWeather.Hot => t <= HotUntil ? (t <= ColdFrom ? MascotWeather.Cold : MascotWeather.Mild) : MascotWeather.Hot,
            MascotWeather.Cold => t >= ColdUntil ? (t >= HotFrom ? MascotWeather.Hot : MascotWeather.Mild) : MascotWeather.Cold,
            _ => t >= HotFrom ? MascotWeather.Hot : t <= ColdFrom ? MascotWeather.Cold : MascotWeather.Mild,
        };
        return _state;
    }
}

/// <summary>True once CPU or RAM stayed at or above a limit for a hold time; it clears when both fall clearly below.</summary>
public sealed class SustainedLoadTracker
{
    private readonly double _enter, _exit;
    private readonly TimeSpan _hold;
    private DateTime? _since;
    private bool _active;

    public SustainedLoadTracker(double enterPercent = 95, double exitPercent = 90, TimeSpan? hold = null)
    {
        _enter = enterPercent;
        _exit = exitPercent;
        _hold = hold ?? TimeSpan.FromSeconds(30);
    }

    public bool Active => _active;

    /// <summary>Feeds one reading. A value that is not a number (no reading) breaks the run, so only measured load counts.</summary>
    public bool Update(DateTime now, double cpuPercent, double ramPercent)
    {
        bool high = (double.IsFinite(cpuPercent) && cpuPercent >= _enter) || (double.IsFinite(ramPercent) && ramPercent >= _enter);
        bool clearlyLow = (!double.IsFinite(cpuPercent) || cpuPercent < _exit) && (!double.IsFinite(ramPercent) || ramPercent < _exit);
        if (_active)
        {
            if (clearlyLow) { _active = false; _since = null; }
            return _active;
        }
        if (!high) { _since = null; return false; }
        _since ??= now;
        if (now - _since >= _hold) _active = true;
        return _active;
    }
}
