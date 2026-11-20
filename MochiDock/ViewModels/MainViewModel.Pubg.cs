using System;
using System.Threading;
using System.Threading.Tasks;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.ViewModels;

public partial class MainViewModel
{
    private readonly PubgStatsService _pubg = new();
    private IPubgCredentialStore _pubgKey = new PubgCredentialStore();
    private CancellationTokenSource? _pubgCts;
    private bool _noticeIsGame;

    // The API publishes a match a few minutes after it ends, so the lookup waits first and then tries a few times.
    private static readonly TimeSpan[] PubgSchedule = { TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4) };

    /// <summary>True when a PUBG lookup can run: switched on, a player name and a saved API key.</summary>
    public bool PubgStatsConfigured(out string key)
    {
        key = "";
        if (!_settings.EnablePubgStats || string.IsNullOrWhiteSpace(_settings.PubgPlayerName)) return false;
        try { key = _pubgKey.Read() ?? ""; }
        catch (Exception ex) { AppLog.Warn("Pubg", ex); }
        return !string.IsNullOrWhiteSpace(key);
    }

    /// <summary>Whether an API key is saved (the key itself is never shown or logged).</summary>
    public bool HasPubgKey()
    {
        try { return !string.IsNullOrWhiteSpace(_pubgKey.Read()); }
        catch (Exception ex) { AppLog.Warn("Pubg", ex); return false; }
    }

    public void SavePubgKey(string key) => _pubgKey.Save(key);
    public void RemovePubgKey() => _pubgKey.Remove();

    // Called when a finished session was stored: for a PUBG session, look the match up in the background.
    private void StartPubgFetch(GameSessionRecord record)
    {
        if (!PubgMatchParser.IsPubg(new[] { record.Game }) || !PubgStatsConfigured(out string key)) return;
        _pubgCts?.Cancel();
        _pubgCts?.Dispose();
        _pubgCts = new CancellationTokenSource();
        var token = _pubgCts.Token;
        string name = _settings.PubgPlayerName.Trim(), platform = PubgStatsService.NormalizePlatform(_settings.PubgPlatform);
        var sessionStartUtc = record.StartedAt.ToUniversalTime();
        var sessionEndUtc = DateTime.UtcNow;
        _ = Task.Run(() => RunPubgFetchAsync(record, name, platform, key, sessionStartUtc, sessionEndUtc, token));
    }

    private async Task RunPubgFetchAsync(GameSessionRecord record, string name, string platform, string key, DateTime startUtc, DateTime endUtc, CancellationToken token)
    {
        try
        {
            foreach (var wait in PubgSchedule)
            {
                await Task.Delay(wait, token).ConfigureAwait(false);
                var result = await _pubg.FetchLatestAsync(platform, name, key, startUtc, endUtc, token).ConfigureAwait(false);
                switch (result.Status)
                {
                    case PubgFetchStatus.Found when result.Match != null:
                        await App.Current.Dispatcher.InvokeAsync(() => OnPubgMatchFound(record, result.Match));
                        return;
                    case PubgFetchStatus.KeyRejected:
                        AppLog.Warn("Pubg", "The PUBG API rejected the saved key; no more lookups for this session.");
                        return;
                    case PubgFetchStatus.PlayerNotFound:
                        AppLog.Warn("Pubg", "The PUBG API does not know that player name on this platform (names are case-sensitive); no more lookups for this session.");
                        return;
                    // NotYet, RateLimited and Error are retried at the next time in the schedule.
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Warn("Pubg", ex); }
    }

    private void OnPubgMatchFound(GameSessionRecord record, GameMatchSummary match)
    {
        record.Match = match;
        _gameSessions.Update();
        RefreshRecentGameSessions();
        ShowMatchResultMoment(match);
        // A short capsule notice (the same slot as the hardware and reminder notices), unless a game is being played now.
        if (_wasGaming || _isWorkstationLocked || !_displayAwake) return;
        _noticeIsAi = false;
        _noticeIsAgenda = false;
        _noticeIsGame = true;_noticeIsDevice = false; _noticeIsWeather = false;
        _hardwareNoticeText = "PUBG stats ready · " + GameMatchFormat.Line(match).Split(" · ", 3)[0] + " · " + match.Kills + (match.Kills == 1 ? " kill" : " kills");
        _hardwareNoticeUntil = DateTimeOffset.UtcNow.AddSeconds(8);
        _hardwareNoticeTimer.Stop();
        _hardwareNoticeTimer.Start();
        NotifyHardwareNotice();
        SoundService.PlayNotificationSound();   // does nothing while notification sounds are off
    }

    private void StopPubgFetch()
    {
        _pubgCts?.Cancel();
        _pubgCts?.Dispose();
        _pubgCts = null;
    }
}
