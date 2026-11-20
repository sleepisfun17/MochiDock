using MochiDock.Core;
using static MochiDock.Core.MascotTriggers;

public class MascotActivityFaceTests
{
    static readonly DateTime Noon = new(2026, 11, 20, 12, 0, 0), Night = new(2026, 11, 20, 22, 0, 0);

    static MascotMood Choose(MascotMood b, MascotMood? activity, bool expressive = true, bool overdue = false, bool gpuHot = false,
        bool overloaded = false, bool battery = false, MascotWeather weather = MascotWeather.Mild, bool petted = false) =>
        MascotMoodPolicy.Choose(new(b, expressive, overdue, gpuHot, overloaded, battery, weather, petted, false, activity));

    [Fact]
    public void EachCardHasItsFace()
    {
        Assert.Equal(MascotMood.Electric, ActivityFace(false, true, DockView.StatsBattery, null, null, Noon));
        Assert.Equal(MascotMood.Money, ActivityFace(false, true, DockView.Fx, 0.4m, null, Noon));
        Assert.Equal(MascotMood.MoneyDown, ActivityFace(false, true, DockView.Fx, -0.5m, null, Noon));
        Assert.Equal(MascotMood.Rainy, ActivityFace(false, true, DockView.Weather, null, "Rain", Noon));
        Assert.Null(ActivityFace(false, true, DockView.Other, null, null, Noon));
    }

    [Fact]
    public void CardFacesNeedTheDockOpenButAFocusSessionDoesNot()
    {
        Assert.Null(ActivityFace(false, false, DockView.StatsBattery, null, null, Noon));
        Assert.Equal(MascotMood.Studying, ActivityFace(true, false, DockView.Other, null, null, Noon));
        Assert.Equal(MascotMood.Studying, ActivityFace(true, true, DockView.Fx, 1m, null, Noon));
    }

    [Theory]
    [InlineData(null, MascotMood.Money)]
    [InlineData("0", MascotMood.Money)]
    [InlineData("-0.09", MascotMood.Money)]
    [InlineData("-0.1", MascotMood.MoneyDown)]
    [InlineData("-3", MascotMood.MoneyDown)]
    public void MoneyIsSadOnlyWhenTheRateClearlyFell(string? change, MascotMood expected) =>
        Assert.Equal(expected, MoneyFace(change is null ? null : decimal.Parse(change, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("Rain", MascotMood.Rainy)]
    [InlineData("Drizzle", MascotMood.Rainy)]
    [InlineData("Thunder", MascotMood.Rainy)]
    [InlineData("Snow", MascotMood.Snowy)]
    [InlineData("Clear", MascotMood.Sunny)]
    [InlineData("PartlyCloudy", MascotMood.Sunny)]
    [InlineData("Cloudy", MascotMood.Cloudy)]
    [InlineData("Fog", MascotMood.Cloudy)]
    public void SkyFacesByDay(string kind, MascotMood expected) => Assert.Equal(expected, WeatherFace(kind, Noon));

    [Fact]
    public void AClearNightIsCloudyAndUnknownWeatherHasNoFace()
    {
        Assert.Equal(MascotMood.Cloudy, WeatherFace("Clear", Night));
        Assert.Null(WeatherFace(null, Noon));
        Assert.Null(WeatherFace("Volcano", Noon));
    }

    [Fact]
    public void TheActivityFaceBeatsMusicAndTheWeather()
    {
        Assert.Equal(MascotMood.Electric, Choose(MascotMood.Music, MascotMood.Electric));
        Assert.Equal(MascotMood.Money, Choose(MascotMood.Idle, MascotMood.Money, weather: MascotWeather.Cold));
        Assert.Equal(MascotMood.Studying, Choose(MascotMood.Bored, MascotMood.Studying));
    }

    [Fact]
    public void WarningsNotificationsGamesAndSleepStillWin()
    {
        Assert.Equal(MascotMood.Hot, Choose(MascotMood.Idle, MascotMood.Electric, gpuHot: true));
        Assert.Equal(MascotMood.Angry, Choose(MascotMood.Idle, MascotMood.Money, overdue: true));
        Assert.Equal(MascotMood.Drained, Choose(MascotMood.Idle, MascotMood.Electric, battery: true));
        Assert.Equal(MascotMood.Dizzy, Choose(MascotMood.Idle, MascotMood.Electric, petted: true));
        Assert.Equal(MascotMood.Excited, Choose(MascotMood.Excited, MascotMood.Electric));
        Assert.Equal(MascotMood.Focused, Choose(MascotMood.Focused, MascotMood.Electric));
        Assert.Equal(MascotMood.Sleeping, Choose(MascotMood.Sleeping, MascotMood.Electric));
        Assert.Equal(MascotMood.Cheering, Choose(MascotMood.Cheering, MascotMood.Studying));
        Assert.Equal(MascotMood.Happy, Choose(MascotMood.Happy, MascotMood.Money));
    }

    [Fact]
    public void ExpressiveOffSkipsTheActivityFaces() => Assert.Equal(MascotMood.Idle, Choose(MascotMood.Idle, MascotMood.Electric, expressive: false));
}
