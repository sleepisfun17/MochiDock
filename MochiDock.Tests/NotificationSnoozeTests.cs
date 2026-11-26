using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class NotificationSnoozeTests
{
    static readonly DateTime Now = new(2026, 11, 24, 14, 30, 0); // a Tuesday

    [Fact]
    public void NothingSnoozedOffersToSnooze()
    {
        Assert.Equal("Snooze Notifications", NotificationSnooze.TrayLabel(null, Now));
        Assert.Equal("Snooze Notifications", NotificationSnooze.TrayLabel(Now.AddMinutes(-1), Now)); // already over
        Assert.Equal("Snooze Notifications", NotificationSnooze.TrayLabel(Now, Now));
    }

    [Fact]
    public void ASnoozeEndingTodayShowsItsTime() =>
        Assert.Equal("Notifications snoozed until 15:30", NotificationSnooze.TrayLabel(Now.AddHours(1), Now));

    [Fact]
    public void ASnoozeEndingLaterShowsTheDayToo() =>
        Assert.Equal("Notifications snoozed until Wed 02:30", NotificationSnooze.TrayLabel(Now.AddHours(12), Now));

    [Theory]
    [InlineData("Email", true, false, false, false, false)]
    [InlineData("Discord", true, false, false, false, true)]
    [InlineData("Discord", false, true, false, false, false)]
    [InlineData("Apps", false, false, true, false, false)]
    [InlineData("WhatsApp", false, false, false, true, false)]
    [InlineData("WhatsApp", true, true, true, false, true)]
    public void MutePolicySuppressesMatchingSourceOnly(string app, bool muteEmail, bool muteDiscord, bool muteApps, bool muteWhatsApp, bool expectedAllowed)
    {
        bool allowed = NotificationMutePolicy.IsAllowed(
            app, Now, DateTime.MinValue, muteEmail, muteDiscord, muteApps, muteWhatsApp,
            quietHoursEnabled: false, quietStart: "22:00", quietEnd: "07:00");
        Assert.Equal(expectedAllowed, allowed);
    }

    [Fact]
    public void MutePolicySuppressesAllDuringSnoozeOrQuietHours()
    {
        Assert.False(NotificationMutePolicy.IsAllowed(
            "WhatsApp", Now, Now.AddMinutes(30), false, false, false, false, false, "22:00", "07:00"));
        Assert.False(NotificationMutePolicy.IsAllowed(
            "Email", new DateTime(2026, 11, 24, 23, 0, 0), DateTime.MinValue, false, false, false, false, true, "22:00", "07:00"));
    }

    [Fact]
    public void PruneRemovesSuppressedItemsAndKeepsAllowedOnes()
    {
        var queue = new List<string> { "WhatsApp", "Email", "Discord", "Media" };
        bool changed = NotificationMutePolicy.Prune(queue, x => x == "Media" ? null : x, app => app == "Discord");
        Assert.True(changed);
        Assert.Equal(new[] { "Discord", "Media" }, queue);
        Assert.False(NotificationMutePolicy.Prune(queue, x => x == "Media" ? null : x, app => app == "Discord"));
    }
}
