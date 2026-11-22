namespace MochiDock.Core;

/// <summary>Pure visibility rules for media card lyrics actions.</summary>
public static class LyricsPolicy
{
    public static bool CanRetryLyrics(bool hasActiveMedia, bool enableSyncedLyrics, bool isLoading, bool hasLyrics) =>
        hasActiveMedia && enableSyncedLyrics && !isLoading && !hasLyrics;
}
