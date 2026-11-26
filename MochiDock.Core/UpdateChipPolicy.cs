namespace MochiDock.Core;

/// <summary>What the update button in the quick settings header shows, and what a click on it does.</summary>
public enum UpdateChipState
{
    /// <summary>No update known: shows the running version; a click checks now.</summary>
    Current,
    /// <summary>Checking right now: shows "Checking…" and ignores clicks.</summary>
    Checking,
    /// <summary>An update this copy can install itself: a click installs (after a confirmation).</summary>
    Install,
    /// <summary>An update that has to be installed by hand: a click opens the release page.</summary>
    Available,
    /// <summary>The setup is downloading: shows progress; cancel from the update banner or Settings.</summary>
    Downloading,
    /// <summary>The setup is starting and MochiDock is about to restart: no click.</summary>
    Installing,
}

public static class UpdateChipPolicy
{
    public static UpdateChipState State(bool checking, bool hasOffer, bool canInstall, bool downloading, bool installing) =>
        downloading ? UpdateChipState.Downloading :
        installing ? UpdateChipState.Installing :
        hasOffer ? (canInstall ? UpdateChipState.Install : UpdateChipState.Available) :
        checking ? UpdateChipState.Checking :
        UpdateChipState.Current;

    /// <summary>Short text for the button; <paramref name="percent"/> is the download progress, when known.</summary>
    public static string Text(UpdateChipState state, string currentVersion, string? offerVersion, int? percent) => state switch
    {
        UpdateChipState.Checking => "Checking…",
        UpdateChipState.Install => $"↑ Update to {offerVersion}",
        UpdateChipState.Available => $"↑ {offerVersion} available",
        UpdateChipState.Downloading => percent is { } p ? $"Downloading {Math.Clamp(p, 0, 100)}%" : "Downloading…",
        UpdateChipState.Installing => "Installing…",
        _ => "v" + currentVersion,
    };

    /// <summary>True when the button should use the accent style: an update is waiting.</summary>
    public static bool IsHighlighted(UpdateChipState state) => state is UpdateChipState.Install or UpdateChipState.Available;

    /// <summary>True when clicking the header chip should perform an action (check, install, or open the release page).</summary>
    public static bool IsInteractive(UpdateChipState state) =>
        state is UpdateChipState.Current or UpdateChipState.Install or UpdateChipState.Available;
}
