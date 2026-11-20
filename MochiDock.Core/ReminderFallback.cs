using System;

namespace MochiDock.Core;

/// <summary>What to do about a due reminder while nothing on screen can show it.</summary>
public sealed record ReminderFallback(bool Sound, bool Notification)
{
    public static readonly ReminderFallback None = new(false, false);
    public bool Any => Sound || Notification;
}

/// <summary>
/// When a fullscreen app hides the dock a reminder cannot appear in the capsule. The fallback gives a sound and/or a Windows
/// notification, once per reminder; it never answers snooze, a lock, a turned-off display or another reason, and it never repeats.
/// </summary>
public static class ReminderFallbackPolicy
{
    public const string Off = "Off", Sound = "Sound", SoundAndNotification = "SoundAndNotification";

    /// <summary>The modes offered in settings; anything unknown counts as the default.</summary>
    public static string NormalizeMode(string? mode) =>
        string.Equals(mode, Off, StringComparison.OrdinalIgnoreCase) ? Off :
        string.Equals(mode, Sound, StringComparison.OrdinalIgnoreCase) ? Sound : SoundAndNotification;

    public static ReminderFallback Decide(string? mode, string? blockedReason, bool alreadyFired)
    {
        if (alreadyFired || blockedReason != AgendaReminderPolicy.DockHiddenReason) return ReminderFallback.None;
        return NormalizeMode(mode) switch
        {
            Off => ReminderFallback.None,
            Sound => new ReminderFallback(true, false),
            _ => new ReminderFallback(true, true),
        };
    }
}
