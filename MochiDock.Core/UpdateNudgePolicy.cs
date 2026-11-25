namespace MochiDock.Core;

/// <summary>
/// Whether an available update is nudged persistently (capsule dot and expanded-dock banner): not for a version the user
/// dismissed, and not while it is being installed. Same trimmed, case-insensitive comparison as <see cref="UpdateAnnouncePolicy"/>.
/// </summary>
public static class UpdateNudgePolicy
{
    public static bool ShouldNudge(string? offerVersion, string? dismissedVersion, bool installing) =>
        !installing && !string.IsNullOrWhiteSpace(offerVersion) &&
        !string.Equals(offerVersion.Trim(), dismissedVersion?.Trim(), StringComparison.OrdinalIgnoreCase);
}
