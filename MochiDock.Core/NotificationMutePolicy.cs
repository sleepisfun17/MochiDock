namespace MochiDock.Core;

/// <summary>Pure notification suppression checks and queue pruning across mute toggles, snooze, and quiet hours.</summary>
public static class NotificationMutePolicy
{
    public static bool IsAllowed(
        string app,
        DateTime now,
        DateTime snoozedUntil,
        bool muteEmail,
        bool muteDiscord,
        bool muteApps,
        bool muteWhatsApp,
        bool quietHoursEnabled,
        string? quietStart,
        string? quietEnd)
    {
        if (now < snoozedUntil) return false;
        if (app == "Email" && muteEmail) return false;
        if (app == "Discord" && muteDiscord) return false;
        if (app == "Apps" && muteApps) return false;
        if (app == "WhatsApp" && muteWhatsApp) return false;
        if (quietHoursEnabled && SettingsValidation.IsQuietTime(now.TimeOfDay, quietStart, quietEnd)) return false;
        return true;
    }

    public static bool Prune<T>(List<T> queue, Func<T, string?> appNameFor, Func<string, bool> isAllowed)
    {
        int before = queue.Count;
        queue.RemoveAll(item => appNameFor(item) is { } app && !isAllowed(app));
        return queue.Count != before;
    }
}
