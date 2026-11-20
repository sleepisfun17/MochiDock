using MochiDock.Core;
namespace MochiDock.Tests;
public class SolarTests
{
    [Theory]
    [InlineData(7,true)] [InlineData(10,true)] [InlineData(18,false)] [InlineData(23,false)]
    public void TokyoDayAndNight(int hour,bool daylight)
    {
        var instant=new DateTimeOffset(2026,11,6,hour,0,0,TimeSpan.FromHours(9));
        Assert.Equal(daylight,SolarPosition.Elevation(instant,35.68,139.69)>0);
    }
    [Fact] public void MorningRisesTowardNoon()
    {
        var morning=new DateTimeOffset(2026,11,6,8,0,0,TimeSpan.FromHours(9));
        Assert.True(SolarPosition.Elevation(morning,35.68,139.69)<SolarPosition.Elevation(morning.AddHours(3.5),35.68,139.69));
    }
    [Theory] [InlineData(false,false,false)] [InlineData(true,false,true)] [InlineData(true,true,false)]
    public void ExpandedCardsRemainInteractive(bool gaming,bool expanded,bool expected) => Assert.Equal(expected,SolarPosition.ClickThrough(gaming,expanded));
}
