using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class LyricsPolicyTests
{
    [Theory]
    [InlineData(false, true, false, false, false)]  // idle (no active track)
    [InlineData(true, false, false, false, false)]  // synced lyrics disabled
    [InlineData(true, true, true, false, false)]    // currently loading
    [InlineData(true, true, false, true, false)]    // already has synced lyrics
    [InlineData(true, true, false, false, true)]    // active track, enabled, not loading, no synced lyrics
    public void CanRetryLyrics_MatchesExpectedStates(bool hasActiveMedia, bool enableSyncedLyrics, bool isLoading, bool hasLyrics, bool expected)
    {
        Assert.Equal(expected, LyricsPolicy.CanRetryLyrics(hasActiveMedia, enableSyncedLyrics, isLoading, hasLyrics));
    }
}
