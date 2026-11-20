using MochiDock.Core;

public class MascotMoodPolicyTests
{
    static MascotInputs In(MascotMood b = MascotMood.Idle, bool expressive = true, bool overdue = false, bool gpuHot = false, bool overloaded = false, bool battery = false, MascotWeather weather = MascotWeather.Mild) =>
        new(b, expressive, overdue, gpuHot, overloaded, battery, weather);

    [Theory]
    [InlineData(MascotMood.Focused)]
    [InlineData(MascotMood.Worried)]
    [InlineData(MascotMood.BreakTime)]
    [InlineData(MascotMood.Alert)]
    public void GamingAndAlertAlwaysWin(MascotMood b) =>
        Assert.Equal(b, MascotMoodPolicy.Choose(In(b, overdue: true, gpuHot: true, overloaded: true, battery: true, weather: MascotWeather.Cold)));

    [Fact]
    public void SleepingBeatsEverythingButAnOverload()
    {
        Assert.Equal(MascotMood.Sleeping, MascotMoodPolicy.Choose(In(MascotMood.Sleeping, weather: MascotWeather.Cold)));   // the weather does not wake Mochi
        Assert.Equal(MascotMood.Sleeping, MascotMoodPolicy.Choose(In(MascotMood.Sleeping)));
        Assert.Equal(MascotMood.Sleeping, MascotMoodPolicy.Choose(In(MascotMood.Sleeping, expressive: false, overloaded: true)));   // expressive off keeps the requested mood
    }

    [Fact]
    public void TheLastingAmbientFacesWakeASleepingMochiInTheirOwnOrder()
    {
        Assert.Equal(MascotMood.Angry, MascotMoodPolicy.Choose(In(MascotMood.Sleeping, overdue: true, gpuHot: true, overloaded: true, battery: true)));
        Assert.Equal(MascotMood.Hot, MascotMoodPolicy.Choose(In(MascotMood.Sleeping, gpuHot: true, overloaded: true, battery: true)));
        Assert.Equal(MascotMood.Dizzy, MascotMoodPolicy.Choose(In(MascotMood.Sleeping, overloaded: true, battery: true)));
        Assert.Equal(MascotMood.Drained, MascotMoodPolicy.Choose(In(MascotMood.Sleeping, battery: true)));
    }

    [Fact]
    public void ALockedPcOrADarkScreenAlwaysSleeps() =>
        Assert.Equal(MascotMood.Sleeping, MascotMoodPolicy.Choose(new(MascotMood.Sleeping, true, true, true, true, true, MascotWeather.Hot, ScreenOff: true)));

    [Fact]
    public void AmbientMoodsFollowTheirPriority()
    {
        Assert.Equal(MascotMood.Angry, MascotMoodPolicy.Choose(In(overdue: true, gpuHot: true, overloaded: true, battery: true, weather: MascotWeather.Hot)));
        Assert.Equal(MascotMood.Hot, MascotMoodPolicy.Choose(In(gpuHot: true, overloaded: true, battery: true)));
        Assert.Equal(MascotMood.Dizzy, MascotMoodPolicy.Choose(In(overloaded: true, battery: true)));
        Assert.Equal(MascotMood.Drained, MascotMoodPolicy.Choose(In(battery: true, weather: MascotWeather.Cold)));
    }

    [Fact]
    public void MusicBeatsTheWeatherButNotTheAmbientMoods()
    {
        Assert.Equal(MascotMood.Music, MascotMoodPolicy.Choose(In(MascotMood.Music, weather: MascotWeather.Hot)));
        Assert.Equal(MascotMood.Angry, MascotMoodPolicy.Choose(In(MascotMood.Music, overdue: true)));
        Assert.Equal(MascotMood.Drained, MascotMoodPolicy.Choose(In(MascotMood.Music, battery: true)));
    }

    [Theory]
    [InlineData(MascotMood.Idle, MascotWeather.Hot, MascotMood.Hot)]
    [InlineData(MascotMood.Waiting, MascotWeather.Cold, MascotMood.Freezing)]
    [InlineData(MascotMood.Bored, MascotWeather.Hot, MascotMood.Hot)]
    [InlineData(MascotMood.Idle, MascotWeather.Mild, MascotMood.Idle)]
    [InlineData(MascotMood.Happy, MascotWeather.Hot, MascotMood.Happy)]      // a happy moment is not interrupted by the weather
    [InlineData(MascotMood.Happy, MascotWeather.Cold, MascotMood.Happy)]
    public void WeatherOnlyChangesAnIdlingMochi(MascotMood b, MascotWeather w, MascotMood expected) =>
        Assert.Equal(expected, MascotMoodPolicy.Choose(In(b, weather: w)));

    [Theory]
    [InlineData(MascotMood.Idle)]
    [InlineData(MascotMood.Music)]
    [InlineData(MascotMood.Happy)]
    public void NothingAmbientKeepsTheRequestedMood(MascotMood b) => Assert.Equal(b, MascotMoodPolicy.Choose(In(b)));

    [Fact]
    public void WithExpressiveOffTheRequestedMoodIsUsedAsIs()
    {
        var all = In(MascotMood.Idle, expressive: false, overdue: true, gpuHot: true, overloaded: true, battery: true, weather: MascotWeather.Hot);
        Assert.Equal(MascotMood.Idle, MascotMoodPolicy.Choose(all));
        Assert.Equal(MascotMood.Music, MascotMoodPolicy.Choose(all with { Base = MascotMood.Music }));
    }

    [Fact]
    public void WeatherHasAGapAroundItsThresholds()
    {
        var t = new MascotWeatherTracker();
        Assert.Equal(MascotWeather.Mild, t.Update(32));
        Assert.Equal(MascotWeather.Hot, t.Update(33));
        Assert.Equal(MascotWeather.Hot, t.Update(32));      // still hot between 31 and 33
        Assert.Equal(MascotWeather.Hot, t.Update(31.5));
        Assert.Equal(MascotWeather.Mild, t.Update(31));
        Assert.Equal(MascotWeather.Cold, t.Update(3));
        Assert.Equal(MascotWeather.Cold, t.Update(4));       // still cold between 3 and 5
        Assert.Equal(MascotWeather.Mild, t.Update(5));
        Assert.Equal(MascotWeather.Cold, t.Update(-2));
        Assert.Equal(MascotWeather.Mild, t.Update(null));    // unknown weather is mild
        Assert.Equal(MascotWeather.Mild, t.Update(double.NaN));
    }

    [Fact]
    public void SustainedLoadNeedsThirtySecondsAndClearsWithAGap()
    {
        var t = new SustainedLoadTracker();
        var start = new DateTime(2026, 11, 14, 12, 0, 0);
        Assert.False(t.Update(start, 97, 40));
        Assert.False(t.Update(start.AddSeconds(29), 97, 40));
        Assert.True(t.Update(start.AddSeconds(30), 97, 40));
        Assert.True(t.Update(start.AddSeconds(40), 92, 40));          // still above the exit level of 90
        Assert.False(t.Update(start.AddSeconds(50), 80, 40));
        Assert.False(t.Update(start.AddSeconds(51), 97, 40));         // a new run starts over
    }

    [Fact]
    public void ADipBreaksTheRunAndRamCountsToo()
    {
        var t = new SustainedLoadTracker();
        var start = new DateTime(2026, 11, 14, 12, 0, 0);
        t.Update(start, 97, 10);
        t.Update(start.AddSeconds(20), 50, 10);                       // below the limit: the run is broken
        Assert.False(t.Update(start.AddSeconds(35), 97, 10));
        Assert.False(t.Update(start.AddSeconds(50), 97, 10));
        Assert.True(t.Update(start.AddSeconds(66), 10, 99));           // RAM alone keeps it high
        Assert.False(new SustainedLoadTracker().Update(start, double.NaN, double.NaN));
    }

    [Fact]
    public void PettingTooMuchBeatsEverythingButSleepingAndGaming()
    {
        MascotInputs Petted(MascotMood b, bool expressive = true) => new(b, expressive, true, true, true, true, MascotWeather.Hot, PettedTooMuch: true);
        Assert.Equal(MascotMood.Sleeping, MascotMoodPolicy.Choose(new(MascotMood.Sleeping, true, false, false, false, false, MascotWeather.Hot, PettedTooMuch: true)));   // nothing ambient: still asleep, petting does not wake it
        Assert.Equal(MascotMood.Focused, MascotMoodPolicy.Choose(Petted(MascotMood.Focused)));
        Assert.Equal(MascotMood.Dizzy, MascotMoodPolicy.Choose(Petted(MascotMood.Alert)));
        Assert.Equal(MascotMood.Dizzy, MascotMoodPolicy.Choose(Petted(MascotMood.Idle)));
        Assert.Equal(MascotMood.Dizzy, MascotMoodPolicy.Choose(Petted(MascotMood.Happy)));
        Assert.Equal(MascotMood.Dizzy, MascotMoodPolicy.Choose(Petted(MascotMood.Music)));
        Assert.Equal(MascotMood.Idle, MascotMoodPolicy.Choose(Petted(MascotMood.Idle, expressive: false)));   // expressive off keeps the normal petting
    }

    [Theory]
    [InlineData(MascotMood.Victory)]
    [InlineData(MascotMood.Defeat)]
    [InlineData(MascotMood.Surprised)]
    [InlineData(MascotMood.Cheering)]
    [InlineData(MascotMood.Praying)]
    [InlineData(MascotMood.Excited)]
    public void AMatchResultBeatsTheLastingFacesButNotSleepingGamingOrPetting(MascotMood result)
    {
        Assert.Equal(result, MascotMoodPolicy.Choose(In(result, overdue: true, gpuHot: true, overloaded: true, battery: true, weather: MascotWeather.Cold)));
        Assert.Equal(MascotMood.Dizzy, MascotMoodPolicy.Choose(new MascotInputs(result, true, false, false, false, false, MascotWeather.Mild, PettedTooMuch: true)));
        Assert.Equal(result, MascotMoodPolicy.Choose(In(result, expressive: false)));
    }
}
