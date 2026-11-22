using System;
using System.Linq;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.ViewModels;

public partial class MainViewModel
{
    private readonly GamePlaytimeStore _playtime = new();
    private DateTime? _playStart;
    private string _playName = "";
    private DateTime? _lastBreakReminder;
    private DateTime _breakNoticeUntilUtc;
    private bool _breakNoticeShown;
    private const double MinPlaytimeSeconds = 30;
    private const int PlaytimeRowsShown = 5;

    private string _playtimeTooltip = "";
    /// <summary>The most played games this week, one per line, for the tooltip of the playtime row.</summary>
    public string PlaytimeTooltip { get => _playtimeTooltip; private set { _playtimeTooltip = value; OnPropertyChanged(); } }
    private string _playtimeTodayText = "0m", _playtimeWeekText = "0m";
    /// <summary>Durations only ("1h 20m"); the Games page puts the "Today" and "This week" labels next to them.</summary>
    public string PlaytimeTodayText { get => _playtimeTodayText; private set { _playtimeTodayText = value; OnPropertyChanged(); } }
    public string PlaytimeWeekText { get => _playtimeWeekText; private set { _playtimeWeekText = value; OnPropertyChanged(); } }
    public bool HasPlaytime => _playtime.Ledger.Games.Count > 0;

    private void InitPlaytime() => RefreshPlaytime();

    private void RefreshPlaytime()
    {
        var now = DateTime.Now;
        var ledger = _playtime.Ledger;
        PlaytimeTodayText = PlaytimeFormat.Duration(ledger.Today(now));
        PlaytimeWeekText = PlaytimeFormat.Duration(ledger.ThisWeek(now, _settings.WeekStartsOnMonday));
        var top = ledger.Between(GamePlaytimeLedger.WeekStart(now, _settings.WeekStartsOnMonday), now).Take(PlaytimeRowsShown).ToList();
        PlaytimeTooltip = top.Count == 0 ? "" : "Most played this week" + string.Concat(top.Select(t => Environment.NewLine + t.Game + "  " + PlaytimeFormat.Duration(t.Seconds)));
        OnPropertyChanged(nameof(HasPlaytime));
    }

    /// <summary>Called by the hardware poll timer: tracks when a gaming stretch starts and ends and shows the break reminder.</summary>
    private void UpdatePlaytime(bool isGaming, bool gamingStateChanged)
    {
        var now = DateTime.Now;
        if (gamingStateChanged && isGaming)
        {
            _playStart = now;
            _playName = "";
            _lastBreakReminder = null;
        }

        if (isGaming && _playStart is { } start)
        {
            string name = _hardwareService.CurrentGameName;
            if (!string.IsNullOrWhiteSpace(name)) _playName = name;

            if (BreakReminder.Due(now, start, _lastBreakReminder, _settings.BreakReminderMinutes))
            {
                _lastBreakReminder = now;
                _breakNoticeUntilUtc = DateTime.UtcNow.AddSeconds(12);
                _breakNoticeShown = true;
                SoundService.PlayNotificationSound();   // does nothing while notification sounds are off
                OnPropertyChanged(nameof(CompactCapsuleText));
            }
        }

        if (_breakNoticeShown && DateTime.UtcNow >= _breakNoticeUntilUtc)
        {
            _breakNoticeShown = false;
            OnPropertyChanged(nameof(CompactCapsuleText));
        }

        if (gamingStateChanged && !isGaming) EndPlaytimeSession();
    }

    /// <summary>Adds the running gaming stretch to the playtime ledger. Also called on exit.</summary>
    private void EndPlaytimeSession()
    {
        if (_playStart is not { } start) return;
        _playStart = null;
        _breakNoticeShown = false;
        var now = DateTime.Now;
        if ((now - start).TotalSeconds < MinPlaytimeSeconds) return;   // a flicker of the game detection is not playtime
        _playtime.Add(_playName, start, now);
        RefreshPlaytime();
    }

    // "1h 05m played - time for a break?" for a few seconds in the gaming capsule.
    private string? BreakNoticeText()
    {
        if (!_breakNoticeShown || DateTime.UtcNow >= _breakNoticeUntilUtc || _playStart is not { } start) return null;
        return $"{PlaytimeFormat.Duration((DateTime.Now - start).TotalSeconds)} played · time for a break?";
    }

    /// <summary>Deletes the playtime history.</summary>
    public void ClearPlaytime()
    {
        _playtime.Clear();
        RefreshPlaytime();
    }
}
