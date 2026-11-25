namespace MochiDock.Core;

/// <summary>
/// Whether a found update gets a capsule notice: once per version, and only from the automatic check (after "Check now" the user is
/// already looking at the result).
/// </summary>
public static class UpdateAnnouncePolicy
{
    public static bool ShouldAnnounce(string? offerVersion, string? lastAnnounced, bool manual) =>
        !manual && !string.IsNullOrWhiteSpace(offerVersion) &&
        !string.Equals(offerVersion.Trim(), lastAnnounced?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the compact capsule is visible and free to show the update pop-up (deferred while expanded, gaming, locked, etc.).</summary>
    public static bool CanShowCapsule(
        bool isExpanded,
        bool presentationSuppressed,
        bool gaming,
        bool workstationLocked,
        bool displayAwake,
        bool notificationInPill,
        bool prayerVisible,
        bool noticeVisible,
        bool notificationsAllowed) =>
        !isExpanded && !presentationSuppressed && !gaming && !workstationLocked && displayAwake &&
        !notificationInPill && !prayerVisible && !noticeVisible && notificationsAllowed;
}
