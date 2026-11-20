using MochiDock.Core;

public class ReminderFallbackPolicyTests
{
    const string Hidden = AgendaReminderPolicy.DockHiddenReason;

    [Theory]
    [InlineData("SoundAndNotification", true, true)]
    [InlineData("Sound", true, false)]
    [InlineData("Off", false, false)]
    [InlineData("nonsense", true, true)]     // unknown values count as the default
    [InlineData(null, true, true)]
    public void ModeDecidesSoundAndNotificationForAHiddenDock(string? mode, bool sound, bool notification)
    {
        var d = ReminderFallbackPolicy.Decide(mode, Hidden, alreadyFired: false);
        Assert.Equal((sound, notification), (d.Sound, d.Notification));
    }

    [Theory]
    [InlineData("snooze or quiet hours")]
    [InlineData("the PC is locked")]
    [InlineData("the display is off")]
    [InlineData("another notification is showing")]
    [InlineData("a prayer reminder is showing")]
    [InlineData(null)]
    public void OnlyAHiddenDockTriggersTheFallback(string? reason) =>
        Assert.False(ReminderFallbackPolicy.Decide("SoundAndNotification", reason, false).Any);

    [Fact]
    public void ItFiresOncePerReminder() =>
        Assert.False(ReminderFallbackPolicy.Decide("SoundAndNotification", Hidden, alreadyFired: true).Any);

    [Fact]
    public void SnoozeWinsOverAHiddenDock()
    {
        // BlockedBy names snooze or quiet hours first, so a snoozed reminder never makes a sound behind a fullscreen app.
        string? reason = AgendaReminderPolicy.BlockedBy(dockHidden: true, locked: false, displayOff: false, notificationShowing: false, prayerShowing: false, snoozedOrQuiet: true);
        Assert.False(ReminderFallbackPolicy.Decide("SoundAndNotification", reason, false).Any);
        Assert.True(ReminderFallbackPolicy.Decide("SoundAndNotification", AgendaReminderPolicy.BlockedBy(true, false, false, false, false, false), false).Any);
    }

    [Theory]
    [InlineData("Off", "Off")]
    [InlineData("sound", "Sound")]
    [InlineData("SOUNDANDNOTIFICATION", "SoundAndNotification")]
    [InlineData("", "SoundAndNotification")]
    public void ModeNamesAreNormalised(string given, string expected) => Assert.Equal(expected, ReminderFallbackPolicy.NormalizeMode(given));
}
