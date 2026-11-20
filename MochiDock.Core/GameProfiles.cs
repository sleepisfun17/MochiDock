using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>
/// What to change while one game is being played. Every part is opt-in: an empty resolution changes nothing, and "Default"
/// keeps the global setting.
/// </summary>
public sealed class GameProfile
{
    /// <summary>The game's process name or window title, as MochiDock shows it (matched ignoring case and a ".exe" ending).</summary>
    public string Game { get; set; } = "";
    /// <summary>"2304x1440" to switch to while the game runs and restore afterwards; empty for no change.</summary>
    public string Resolution { get; set; } = "";
    /// <summary>"Default", "On" or "Off": overrides "Click-through in games" for this game.</summary>
    public string ClickThrough { get; set; } = GameProfiles.Default;
    /// <summary>"Default", "Hold" (queue notifications for the summary) or "Allow" (show them), overriding "Do not disturb while gaming".</summary>
    public string Notifications { get; set; } = GameProfiles.Default;
}

/// <summary>Pure rules for per-game profiles.</summary>
public static class GameProfiles
{
    public const string Default = "Default", On = "On", Off = "Off", Hold = "Hold", Allow = "Allow";

    /// <summary>The comparison form of a game name: trimmed, lower case, without a trailing ".exe".</summary>
    public static string Key(string? name)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^4];
        return trimmed.Trim().ToLowerInvariant();
    }

    /// <summary>The profile whose game equals any of the names being played (the frame-capture title, the process name...).</summary>
    public static GameProfile? Find(IEnumerable<GameProfile>? profiles, IEnumerable<string> names)
    {
        if (profiles == null) return null;
        var keys = names.Select(Key).Where(k => k.Length > 0).ToHashSet();
        return keys.Count == 0 ? null : profiles.FirstOrDefault(p => Key(p.Game) is { Length: > 0 } k && keys.Contains(k));
    }

    public static string NormalizeClickThrough(string? value) =>
        string.Equals(value, On, StringComparison.OrdinalIgnoreCase) ? On : string.Equals(value, Off, StringComparison.OrdinalIgnoreCase) ? Off : Default;

    public static string NormalizeNotifications(string? value) =>
        string.Equals(value, Hold, StringComparison.OrdinalIgnoreCase) ? Hold : string.Equals(value, Allow, StringComparison.OrdinalIgnoreCase) ? Allow : Default;

    /// <summary>The click-through setting to use: the profile's choice, else the global one.</summary>
    public static bool ClickThroughEnabled(bool globalSetting, GameProfile? profile) => NormalizeClickThrough(profile?.ClickThrough) switch
    {
        On => true,
        Off => false,
        _ => globalSetting,
    };

    /// <summary>Whether notifications are held for the summary during the game: the profile's choice, else "Do not disturb while gaming".</summary>
    public static bool HoldNotifications(bool globalDoNotDisturb, GameProfile? profile) => NormalizeNotifications(profile?.Notifications) switch
    {
        Hold => true,
        Allow => false,
        _ => globalDoNotDisturb,
    };

    /// <summary>The resolution to switch to for a profile, or null when it has none, it cannot be read, or the display already uses it.</summary>
    public static (int Width, int Height)? ResolutionToApply(GameProfile? profile, int currentWidth, int currentHeight)
    {
        if (profile == null || !DisplayResolutionPolicy.TryParse(profile.Resolution, out int w, out int h)) return null;
        return w == currentWidth && h == currentHeight ? null : (w, h);
    }

    /// <summary>
    /// The resolution to go back to when the game ends: the one before the switch, but only while the display still uses the
    /// resolution the profile set (a resolution you changed yourself during the game is left alone).
    /// </summary>
    public static (int Width, int Height)? ResolutionToRestore((int Width, int Height) before, (int Width, int Height) applied, int currentWidth, int currentHeight) =>
        currentWidth == applied.Width && currentHeight == applied.Height && before != applied ? before : null;

    /// <summary>Adds or replaces the profile of a game (by comparison key), keeping the list free of duplicates.</summary>
    public static List<GameProfile> Upsert(IEnumerable<GameProfile> profiles, GameProfile profile)
    {
        var list = profiles.Where(p => Key(p.Game) != Key(profile.Game)).ToList();
        list.Add(profile);
        return list;
    }
}
