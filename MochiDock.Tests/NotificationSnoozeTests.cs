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
}
