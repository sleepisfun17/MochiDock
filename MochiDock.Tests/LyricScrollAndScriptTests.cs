using MochiDock.Core;

public class LyricScrollPaceTests
{
    [Fact]
    public void ALongLineFinishesBeforeTheNextLine()
    {
        // 260 DIPs of overflow, 4 s on screen, user speed 35 px/s (which alone would need 7.4 s).
        var plan = LyricScrollPace.Plan(260, 4.0, 35);
        Assert.True(plan.StartPause + plan.ScrollSeconds + LyricScrollPace.EndHold <= 4.0 + 1e-9);
        Assert.True(plan.Speed > 35);
    }

    [Fact]
    public void AShortOverflowKeepsTheUserSpeedAndHoldsAtTheEnd()
    {
        var plan = LyricScrollPace.Plan(30, 6.0, 35);
        Assert.Equal(35, plan.Speed, 6);
        Assert.Equal(30 / 35.0, plan.ScrollSeconds, 6);
    }

    [Fact]
    public void SpeedIsCappedForReadability()
    {
        var plan = LyricScrollPace.Plan(2000, 1.5, 35);
        Assert.Equal(LyricScrollPace.MaxSpeed, plan.Speed, 6);
    }

    [Theory]
    [InlineData(0, 4, 35)]
    [InlineData(-10, 4, 35)]
    [InlineData(double.NaN, 4, 35)]
    public void NoOverflowMeansNoScroll(double overflow, double seconds, double speed) =>
        Assert.Equal(0, LyricScrollPace.Plan(overflow, seconds, speed).Speed);

    [Fact]
    public void UnknownLineTimeFallsBackToTheUserSpeed()
    {
        var plan = LyricScrollPace.Plan(100, double.NaN, 40);
        Assert.Equal(40, plan.Speed, 6);
        Assert.Equal(LyricScrollPace.MaxStartPause, plan.StartPause, 6);
    }
}

public class LyricScriptTests
{
    [Theory]
    [InlineData("kimi no koe ga kikoeru yo\nmou ichido dake aitai\nsekai ga owaru made zutto issho ni ite\nkokoro no naka de")]
    [InlineData("Yūki o dashite ashita e mukau\nhikari no naka de kitto mata aeru kara\nnamida wa misenai")]
    public void RomajiLyricsAreRecognised(string text) => Assert.True(LyricScript.LooksLikeRomaji(text));

    [Theory]
    [InlineData("I can't stop thinking about you every night\nWhen the city lights are falling down\nHold me closer, never let me go\nWe will find our way back home")]
    [InlineData("君の声が聞こえる\nもう一度だけ会いたい\nThe night is young and so are we")]
    [InlineData("Inside you, the light is shining\n心の中で 光が\nI will never let it fade away again")]
    [InlineData("too short to judge")]
    [InlineData("")]
    public void EnglishNativeOrMixedLyricsAreNotRomaji(string text) => Assert.False(LyricScript.LooksLikeRomaji(text));

    [Fact]
    public void JapaneseScriptIsDetected()
    {
        Assert.True(LyricScript.HasJapanese("Inside you 心の中で"));
        Assert.False(LyricScript.HasJapanese("kokoro no naka de"));
    }
}
