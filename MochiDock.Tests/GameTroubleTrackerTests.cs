using MochiDock.Core;

public class GameTroubleTrackerTests
{
    static readonly DateTime T0 = new(2026, 11, 15, 20, 0, 0);

    // 40 seconds of a steady 100 fps, one reading a second: a baseline.
    static GameTroubleTracker Steady(out DateTime end)
    {
        var t = new GameTroubleTracker();
        for (int s = 0; s < 40; s++) Assert.False(t.Update(T0.AddSeconds(s), 100, 20));
        end = T0.AddSeconds(40);
        return t;
    }

    [Fact]
    public void ASustainedFpsDropMakesItActiveAfterFiveSeconds()
    {
        var t = Steady(out var now);
        for (int s = 0; s < 5; s++) Assert.False(t.Update(now.AddSeconds(s), 40, 20));   // 40 is below 60 percent of 100, but not yet for 5 s
        Assert.True(t.Update(now.AddSeconds(5), 40, 20));
    }

    [Fact]
    public void AShortDipNeverTriggers()
    {
        var t = Steady(out var now);
        for (int s = 0; s < 4; s++) t.Update(now.AddSeconds(s), 40, 20);
        Assert.False(t.Update(now.AddSeconds(4), 100, 20));    // recovered before 5 s
        for (int s = 5; s < 30; s++) Assert.False(t.Update(now.AddSeconds(s), 100, 20));
    }

    [Fact]
    public void ASmallDropIsNotTrouble()
    {
        var t = Steady(out var now);
        for (int s = 0; s < 30; s++) Assert.False(t.Update(now.AddSeconds(s), 70, 20));   // 70 is above 60 percent of 100
    }

    [Fact]
    public void HighPingTriggersWithoutAnyFpsHistory()
    {
        var t = new GameTroubleTracker();
        for (int s = 0; s < 5; s++) Assert.False(t.Update(T0.AddSeconds(s), 0, 220));
        Assert.True(t.Update(T0.AddSeconds(5), 0, 220));
    }

    [Fact]
    public void ItClearsAfterThreeGoodSeconds()
    {
        var t = new GameTroubleTracker();
        for (int s = 0; s <= 5; s++) t.Update(T0.AddSeconds(s), 0, 220);
        Assert.True(t.Active);
        Assert.True(t.Update(T0.AddSeconds(6), 0, 30));      // good, but only just now
        Assert.True(t.Update(T0.AddSeconds(8), 0, 30));
        Assert.False(t.Update(T0.AddSeconds(9), 0, 30));
    }

    [Fact]
    public void ABadSpikeInsideTheClearTimeKeepsItActive()
    {
        var t = new GameTroubleTracker();
        for (int s = 0; s <= 5; s++) t.Update(T0.AddSeconds(s), 0, 220);
        t.Update(T0.AddSeconds(6), 0, 30);
        t.Update(T0.AddSeconds(7), 0, 220);                   // bad again: the clear timer starts over
        Assert.True(t.Update(T0.AddSeconds(10), 0, 30));
        Assert.False(t.Update(T0.AddSeconds(13), 0, 30));
    }

    [Fact]
    public void MissingReadingsCountAsNothing()
    {
        var t = new GameTroubleTracker();
        for (int s = 0; s < 60; s++) Assert.False(t.Update(T0.AddSeconds(s), 0, -1));   // no fps, no ping
    }

    [Fact]
    public void AGameThatIsAlwaysSlowIsNotInTrouble()
    {
        var t = new GameTroubleTracker();
        for (int s = 0; s < 40; s++) t.Update(T0.AddSeconds(s), 25, 20);                // baseline 25, under the 30 fps floor
        for (int s = 40; s < 70; s++) Assert.False(t.Update(T0.AddSeconds(s), 10, 20));
    }

    [Fact]
    public void ADropIsNotAddedToTheBaseline()
    {
        var t = Steady(out var now);
        for (int s = 0; s < 60; s++) t.Update(now.AddSeconds(s), 40, 20);               // a minute of 40 fps
        Assert.True(t.Active);                                                           // still measured against the old 100
    }

    [Fact]
    public void ResetForgetsEverything()
    {
        var t = new GameTroubleTracker();
        for (int s = 0; s <= 5; s++) t.Update(T0.AddSeconds(s), 0, 220);
        t.Reset();
        Assert.False(t.Active);
        Assert.False(t.Update(T0.AddSeconds(7), 0, 220));
    }
}
