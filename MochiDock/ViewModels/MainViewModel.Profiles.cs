using System;
using System.Linq;
using MochiDock.Core;

namespace MochiDock.ViewModels;

public partial class MainViewModel
{
    private GameProfile? _activeGameProfile;

    /// <summary>
    /// Asks the dock window to switch to (apply = true) or back from (apply = false) the resolution of a game profile.
    /// The window remembers the resolution it left and only restores it while the profile's resolution is still set.
    /// </summary>
    public event Action<int, int, bool>? GameProfileResolutionRequested;

    /// <summary>The profile of the game being played, or null.</summary>
    public GameProfile? ActiveGameProfile => _activeGameProfile;

    /// <summary>Click-through in games: the active profile's choice, else the global setting.</summary>
    public bool EffectiveGamingClickThrough => GameProfiles.ClickThroughEnabled(_settings.EnableGamingClickThrough, _activeGameProfile);

    /// <summary>Whether notifications are held during the game: the active profile's choice, else the global setting.</summary>
    private bool GamingDndActive => GameProfiles.HoldNotifications(_settings.EnableGamingDnd, _activeGameProfile);

    /// <summary>The name to store in a new profile for the game being played, or "" when none is running.</summary>
    public string CurrentGameNameForProfile() => IsGamingSessionActive ? _hardwareService.CurrentGameName : "";

    // Called by the hardware poll timer. A profile is looked up when a game starts and again for the first half minute (the frame
    // capture may only learn the game's title a little later); it is dropped when the game ends.
    private void UpdateGameProfile(bool isGaming, bool gamingStateChanged)
    {
        if (gamingStateChanged && !isGaming)
        {
            EndGameProfile();
            return;
        }
        if (!isGaming || _activeGameProfile != null) return;
        if (!gamingStateChanged && (_playStart is not { } start || (DateTime.Now - start).TotalSeconds > 30)) return;

        var profile = GameProfiles.Find(_settings.GameProfiles, _hardwareService.CurrentGameNames());
        if (profile == null) return;
        _activeGameProfile = profile;
        OnPropertyChanged(nameof(ActiveGameProfile));
        OnPropertyChanged(nameof(EffectiveGamingClickThrough));
        if (DisplayResolutionPolicy.TryParse(profile.Resolution, out int w, out int h)) GameProfileResolutionRequested?.Invoke(w, h, true);
    }

    /// <summary>Drops the active profile and restores its resolution. Also called on exit.</summary>
    private void EndGameProfile()
    {
        var profile = _activeGameProfile;
        if (profile == null) return;
        _activeGameProfile = null;
        OnPropertyChanged(nameof(ActiveGameProfile));
        OnPropertyChanged(nameof(EffectiveGamingClickThrough));
        if (DisplayResolutionPolicy.TryParse(profile.Resolution, out int w, out int h)) GameProfileResolutionRequested?.Invoke(w, h, false);
    }

    /// <summary>Adds or replaces a profile and saves it.</summary>
    public void SaveGameProfile(GameProfile profile) =>
        UpdateSettings(x => x.GameProfiles = GameProfiles.Upsert(x.GameProfiles, profile));

    public void RemoveGameProfile(string game) =>
        UpdateSettings(x => x.GameProfiles.RemoveAll(p => GameProfiles.Key(p.Game) == GameProfiles.Key(game)));
}
