using MochiDock.Core;

public class PettingTrackerTests
{
    static readonly DateTime T0 = new(2026, 11, 14, 12, 0, 0);

    static PettingTracker Spree(int clicks, double secondsBetween = 0.25, PettingTracker? tracker = null)
    {
        tracker ??= new PettingTracker();
        for (int i = 0; i < clicks; i++) tracker.Register(T0.AddSeconds(i * secondsBetween));
        return tracker;
    }

    [Fact]
    public void EightQuickClicksMakeMochiDizzyAndSevenDoNot()
    {
        var seven = Spree(7);
        Assert.False(seven.IsDizzy(T0.AddSeconds(1.75)));
        var eight = Spree(8);
        Assert.True(eight.IsDizzy(T0.AddSeconds(1.75)));
        Assert.Equal(T0.AddSeconds(1.75).Add(PettingTracker.DizzyHold), eight.DizzyUntil);
    }

    [Fact]
    public void RegisterReportsTheStateAfterThisClick()
    {
        var t = new PettingTracker();
        for (int i = 0; i < 7; i++) Assert.False(t.Register(T0.AddSeconds(i * 0.2)));
        Assert.True(t.Register(T0.AddSeconds(1.4)));    // the eighth click
        Assert.True(t.Register(T0.AddSeconds(2)));      // clicking while dizzy keeps him dizzy
    }

    [Fact]
    public void ClicksSpreadOverMoreThanTheWindowNeverTrigger()
    {
        var t = new PettingTracker();
        for (int i = 0; i < 30; i++) Assert.False(t.Register(T0.AddSeconds(i * 0.8)));   // one click every 0.8 s: at most 7 inside 5 s
        Assert.False(t.IsDizzy(T0.AddSeconds(24)));
    }

    [Fact]
    public void TheWindowSlides()
    {
        var t = new PettingTracker();
        for (int i = 0; i < 5; i++) t.Register(T0.AddSeconds(i * 0.2));          // five clicks, then a pause of 6 s
        for (int i = 0; i < 7; i++) Assert.False(t.Register(T0.AddSeconds(7 + i * 0.2)));   // the old five no longer count
        Assert.True(t.Register(T0.AddSeconds(7 + 7 * 0.2)));
    }

    [Fact]
    public void DizzinessEndsThreeSecondsAfterTheLastClick()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), PettingTracker.DizzyHold);
        var t = Spree(8);                                    // dizzy from 1.75 s
        var last = T0.AddSeconds(1.75);
        Assert.True(t.IsDizzy(last.AddSeconds(2.9)));
        Assert.False(t.IsDizzy(last.AddSeconds(3)));
        t.Register(last.AddSeconds(2));                       // a click 2 s later extends it to 3 s after that click
        Assert.True(t.IsDizzy(last.AddSeconds(4.9)));
        Assert.False(t.IsDizzy(last.AddSeconds(5)));
    }

    [Fact]
    public void DizzinessNeverLastsLongerThanSixSecondsInAll()
    {
        Assert.Equal(TimeSpan.FromSeconds(6), PettingTracker.DizzyMax);
        var t = Spree(8);
        var start = T0.AddSeconds(1.75);
        for (int s = 1; s <= 5; s++) Assert.True(t.Register(start.AddSeconds(s)));        // one click a second keeps him dizzy
        Assert.Equal(start.AddSeconds(6), t.DizzyUntil);                                    // capped at 6 s from the start
        Assert.False(t.IsDizzy(start.AddSeconds(6)));
    }

    [Fact]
    public void AfterTheMaximumTheCountStartsAgain()
    {
        var t = Spree(8);
        var start = T0.AddSeconds(1.75);
        for (int s = 1; s <= 5; s++) t.Register(start.AddSeconds(s));
        var now = start.AddSeconds(6);
        Assert.False(t.Register(now));                        // this click is the first of a new count
        for (int i = 1; i < 7; i++) Assert.False(t.Register(now.AddSeconds(i * 0.2)));
        Assert.True(t.Register(now.AddSeconds(7 * 0.2)));     // eight clicks later he is dizzy again
    }

    [Fact]
    public void AClickAfterTheDizzinessEndedStartsAFreshCount()
    {
        var t = Spree(8);
        var later = T0.AddSeconds(30);
        for (int i = 0; i < 7; i++) Assert.False(t.Register(later.AddSeconds(i * 0.2)));
        Assert.True(t.Register(later.AddSeconds(1.4)));
    }
}
