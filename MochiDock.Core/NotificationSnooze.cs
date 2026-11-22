using System;
using System.Globalization;

namespace MochiDock.Core;

/// <summary>Texts for the notification snooze (the tray menu's Snooze item shows when an active snooze ends).</summary>
public static class NotificationSnooze
{
    /// <summary>
    /// "Snooze Notifications" when nothing is snoozed (null or already over), "Notifications snoozed until 18:00" for a snooze
    /// that ends today, and "Notifications snoozed until Tue 09:00" for one that ends on a later day.
    /// </summary>
    public static string TrayLabel(DateTime? until, DateTime now)
    {
        if (until is not { } end || end <= now) return "Snooze Notifications";
        string format = end.Date == now.Date ? "HH:mm" : "ddd HH:mm";
        return "Notifications snoozed until " + end.ToString(format, CultureInfo.InvariantCulture);
    }
}
