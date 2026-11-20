using System;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.ViewModels;

public partial class MainViewModel
{
    private readonly LolLiveClientService _lol = new();
    private static readonly TimeSpan LolSnapshotMaxAge = TimeSpan.FromMinutes(15);

    // Called by the hardware poll timer: reading starts when League of Legends is the game being played and stops when gaming ends
    // (or the setting is turned off). It is only polled during a League match: no request is made otherwise.
    private void UpdateLolStats(bool isGaming)
    {
        if (!isGaming || !_settings.EnableLolMatchStats)
        {
            if (_lol.IsRunning) _lol.Stop();
            return;
        }
        if (!_lol.IsRunning && LolMatchParser.IsLeague(_hardwareService.CurrentGameNames())) _lol.Start();
    }

    // The finished session of League of Legends gets the match the API reported last, when it is recent.
    private void AttachMatch(GameSessionRecord record)
    {
        if (!_settings.EnableLolMatchStats || !LolMatchParser.IsLeague(new[] { record.Game })) return;
        var match = _lol.Latest;
        if (match != null && DateTime.UtcNow - _lol.LatestAtUtc < LolSnapshotMaxAge) record.Match = match;
    }
}
