using MochiDock.Core;

public class CoreBarLayoutTests
{
    [Theory]
    [InlineData(190, 32, 3)]
    [InlineData(238, 32, 4)]   // 125 %
    [InlineData(285, 32, 5)]   // 150 %
    [InlineData(380, 32, 6)]   // 200 %
    [InlineData(190, 4, 3)]
    [InlineData(190, 8, 3)]
    [InlineData(190, 16, 3)]
    [InlineData(190, 24, 3)]
    [InlineData(190, 64, 3)]
    [InlineData(191, 7, 3)]
    public void BarsAreEqualWholePixelsThatFitAndSitCentred(double width, int count, double gap)
    {
        var (bar, g, left) = CoreBarLayout.Compute(width, count, gap);
        Assert.True(bar >= 1);
        Assert.True(g >= 1);
        int used = bar * count + g * (count - 1);
        Assert.True(used <= (int)width, $"{used} > {width}");
        int right = (int)width - used - left;
        Assert.InRange(right - left, 0, 1);   // centred to the pixel
    }

    [Fact]
    public void ThirtyTwoCoresGetTheSameWidthEverywhere()
    {
        var (bar, gap, _) = CoreBarLayout.Compute(190, 32, 3);
        Assert.Equal(3, bar);
        Assert.Equal(3, gap);
    }

    [Fact]
    public void ACrowdedRowShrinksTheGapBeforeTheBars()
    {
        var (bar, gap, _) = CoreBarLayout.Compute(150, 64, 3);
        Assert.True(bar >= 1);
        Assert.Equal(1, gap);
    }

    [Theory]
    [InlineData(0, 32)]
    [InlineData(190, 0)]
    [InlineData(-5, 8)]
    public void NothingToDrawGivesZero(double width, int count) => Assert.Equal((0, 0, 0), CoreBarLayout.Compute(width, count, 3));
}
