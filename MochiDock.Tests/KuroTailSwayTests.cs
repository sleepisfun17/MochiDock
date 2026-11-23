using MochiDock.Core;

public class KuroTailSwayTests
{
    [Theory]
    [InlineData(0.0, 6)]
    [InlineData(0.5, 9)]
    [InlineData(1.0, 12)]
    public void DelayIsSixToTwelveSeconds(double unit, double seconds) =>
        Assert.Equal(seconds, KuroTailSway.NextDelay(unit).TotalSeconds, 6);

    [Theory]
    [InlineData(-3.0, 6)]
    [InlineData(7.0, 12)]
    [InlineData(double.NaN, 9)]
    [InlineData(double.PositiveInfinity, 9)]
    public void DelayIsClamped(double unit, double seconds) =>
        Assert.Equal(seconds, KuroTailSway.NextDelay(unit).TotalSeconds, 6);

    [Theory]
    [InlineData(MascotMood.Idle)]
    [InlineData(MascotMood.Waiting)]
    [InlineData(MascotMood.Happy)]
    [InlineData(MascotMood.Music)]
    [InlineData(MascotMood.Coffee)]
    [InlineData(MascotMood.Proud)]
    [InlineData(MascotMood.Birthday)]
    [InlineData(MascotMood.Cheering)]
    [InlineData(MascotMood.Excited)]
    public void SwaysWhenRelaxedOrHappy(MascotMood mood) => Assert.True(KuroTailSway.CanSway(mood));

    [Theory]
    [InlineData(MascotMood.Sleeping)]
    [InlineData(MascotMood.Focused)]
    [InlineData(MascotMood.Worried)]
    [InlineData(MascotMood.BreakTime)]
    [InlineData(MascotMood.Waking)]
    [InlineData(MascotMood.Bored)]
    [InlineData(MascotMood.Studying)]
    [InlineData(MascotMood.Meeting)]
    [InlineData(MascotMood.Dizzy)]
    [InlineData(MascotMood.Drained)]
    [InlineData(MascotMood.Defeat)]
    [InlineData(MascotMood.Freezing)]
    [InlineData(MascotMood.Hot)]
    [InlineData(MascotMood.Angry)]
    public void StaysStillOtherwise(MascotMood mood) => Assert.False(KuroTailSway.CanSway(mood));
}
