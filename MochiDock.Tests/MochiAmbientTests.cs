using MochiDock.Core;

public class MochiAmbientTests
{
    static MascotMood? Rest(double dockMinutes, double inputMinutes, bool media = false, bool gaming = false, bool locked = false, bool screenOn = true) =>
        MascotTriggers.RestingFace(TimeSpan.FromMinutes(dockMinutes), TimeSpan.FromMinutes(inputMinutes), media, gaming, locked, screenOn);

    [Fact]
    public void WorkingElsewhereRotatesRestingFacesInsteadOfSleeping()
    {
        // Typing (input idle about 0) while the dock is untouched: thoughts, coffee, a sigh, a doze, then round again.
        Assert.Null(Rest(1, 0));                                   // the dock was touched a minute ago
        Assert.Null(Rest(2, 0));                                   // first step: the idle thoughts
        Assert.Equal(MascotMood.Coffee, Rest(4, 0));
        Assert.Equal(MascotMood.Bored, Rest(6, 0));
        Assert.Equal(MascotMood.Sleeping, Rest(8, 0));             // a short doze
        Assert.Null(Rest(10, 0));                                  // back to the thoughts
        Assert.Equal(MascotMood.Coffee, Rest(12, 0));
    }

    [Fact]
    public void NobodyAtThePcForTenMinutesSleeps()
    {
        Assert.Equal(MascotMood.Sleeping, Rest(30, 10));
        Assert.NotEqual(MascotMood.Sleeping, Rest(4, 9.9));
        Assert.Equal(MascotMood.Sleeping, Rest(0.5, 10));          // even if the pointer passed the dock earlier
    }

    [Theory]
    [InlineData(true, false, false, true)]     // music has its own face
    [InlineData(false, true, false, true)]     // a game has its own faces
    [InlineData(false, false, true, true)]     // the lock sets the sleep
    [InlineData(false, false, false, false)]   // so does a dark screen
    public void OtherStatesKeepTheirOwnFaces(bool media, bool gaming, bool locked, bool screenOn) =>
        Assert.Null(Rest(30, 30, media, gaming, locked, screenOn));

    [Fact]
    public void ACardFaceBeatsTheCoffee() =>
        Assert.Equal(MascotMood.Electric, MascotMoodPolicy.Choose(new(MascotMood.Coffee, true, false, false, false, false, MascotWeather.Cold, false, false, MascotMood.Electric)));

    [Fact]
    public void TheColdDoesNotTakeTheCoffeeAway() =>
        Assert.Equal(MascotMood.Coffee, MascotMoodPolicy.Choose(new(MascotMood.Coffee, true, false, false, false, false, MascotWeather.Cold)));

    [Fact]
    public void WakingUpAfterFourHoursAway()
    {
        Assert.False(MascotTriggers.WakingUp(TimeSpan.FromHours(3.9)));
        Assert.True(MascotTriggers.WakingUp(TimeSpan.FromHours(4)));
        Assert.True(MascotTriggers.WakingUp(TimeSpan.FromHours(9)));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(9, true)]
    [InlineData(10, false)]
    [InlineData(23, false)]
    public void TheMorningStartIsFiveToTen(int hour, bool expected) =>
        Assert.Equal(expected, MascotTriggers.WakingOnStart(new DateTime(2026, 11, 15, hour, 30, 0)));

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 6)]
    [InlineData(0.5, 4.5)]
    [InlineData(-4, 3)]       // out of range is clamped
    [InlineData(9, 6)]
    [InlineData(double.NaN, 4.5)]
    public void WinksAreThreeToSixMinutesApart(double unit, double minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), MochiWink.NextDelay(unit));

    [Theory]
    [InlineData(MascotMood.Idle, true)]
    [InlineData(MascotMood.Waiting, true)]
    [InlineData(MascotMood.Music, false)]
    [InlineData(MascotMood.Focused, false)]
    [InlineData(MascotMood.Sleeping, false)]
    [InlineData(MascotMood.Bored, false)]
    [InlineData(MascotMood.Happy, false)]
    public void HeOnlyWinksWhileIdling(MascotMood mood, bool expected) => Assert.Equal(expected, MochiWink.CanWink(mood));

    [Fact]
    public void WakingIsAShortMomentThatBeatsLastingFacesButNotSleeping()
    {
        MascotInputs In(MascotMood b, bool overdue = false) => new(b, true, overdue, false, false, false, MascotWeather.Mild);
        Assert.Equal(MascotMood.Waking, MascotMoodPolicy.Choose(In(MascotMood.Waking, overdue: true)));
        Assert.Equal(MascotMood.Sleeping, MascotMoodPolicy.Choose(In(MascotMood.Sleeping)));
    }

    [Fact]
    public void LonelyIsAnAmbientFaceThatTheLastingFacesStillBeat()
    {
        MascotInputs In(MascotMood b, bool overdue = false, MascotWeather w = MascotWeather.Mild) => new(b, true, overdue, false, false, false, w);
        Assert.Equal(MascotMood.Bored, MascotMoodPolicy.Choose(In(MascotMood.Bored)));
        Assert.Equal(MascotMood.Angry, MascotMoodPolicy.Choose(In(MascotMood.Bored, overdue: true)));
        Assert.Equal(MascotMood.Hot, MascotMoodPolicy.Choose(In(MascotMood.Bored, w: MascotWeather.Hot)));
    }
}
