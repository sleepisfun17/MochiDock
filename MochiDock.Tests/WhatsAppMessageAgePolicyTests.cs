using MochiDock.Core;
namespace MochiDock.Tests;
public class WhatsAppMessageAgePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Settled = TimeSpan.FromMinutes(5);

    [Fact] public void FreshMessagesNotify() =>
        Assert.True(WhatsAppMessageAgePolicy.ShouldNotify(Now.AddSeconds(-5), Now, Settled));

    [Fact] public void MessagesOlderThanTwoMinutesAreIgnored()
    {
        Assert.False(WhatsAppMessageAgePolicy.ShouldNotify(Now.AddMinutes(-3), Now, Settled));
        Assert.False(WhatsAppMessageAgePolicy.ShouldNotify(Now.AddDays(-30), Now, Settled));
    }

    [Fact] public void ClockSkewIntoTheFutureStillNotifies() =>
        Assert.True(WhatsAppMessageAgePolicy.ShouldNotify(Now.AddSeconds(40), Now, Settled));

    [Fact] public void MissingTimeNotifiesOnceSettledButNotRightAfterStart()
    {
        Assert.True(WhatsAppMessageAgePolicy.ShouldNotify(null, Now, Settled));
        Assert.False(WhatsAppMessageAgePolicy.ShouldNotify(null, Now, TimeSpan.FromSeconds(5)));
    }

    [Fact] public void StartupIsStricter()
    {
        var early = TimeSpan.FromSeconds(10);
        Assert.True(WhatsAppMessageAgePolicy.ShouldNotify(Now.AddSeconds(-30), Now, early));
        Assert.False(WhatsAppMessageAgePolicy.ShouldNotify(Now.AddSeconds(-90), Now, early));
        Assert.True(WhatsAppMessageAgePolicy.ShouldNotify(Now.AddSeconds(-90), Now, Settled));
    }

    [Theory]
    [InlineData("2026-11-12T09:59:30Z", 1794477570)]
    [InlineData("1794477570", 1794477570)]
    [InlineData("1794477570000", 1794477570)]
    public void ParsesIsoSecondsAndMilliseconds(string text, long expectedUnixSeconds)
    {
        var parsed = WhatsAppMessageAgePolicy.ParseMessageTime(text);
        Assert.NotNull(parsed);
        Assert.Equal(expectedUnixSeconds, parsed!.Value.ToUnixTimeSeconds());
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("soon")] [InlineData("-5")] [InlineData("0")]
    public void UnreadableTimesAreNull(string? text) =>
        Assert.Null(WhatsAppMessageAgePolicy.ParseMessageTime(text));
}
