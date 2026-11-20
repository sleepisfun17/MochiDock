using MochiDock.Core;

public class MochiAmbientTests
{
    [Theory]
    [InlineData(20, false, false, false, true, true)]     // 20 minutes idle, nothing going on
    [InlineData(19, false, false, false, true, false)]    // not long enough yet
    [InlineData(60, true, false, false, true, false)]     // music is playing
    [InlineData(60, false, true, false, true, false)]     // a game is running
    [InlineData(60, false, false, true, true, false)]     // locked (he sleeps then)
    [InlineData(60, false, false, false, false, false)]   // the screen is off
    [InlineData(0, false, false, false, true, false)]     // zero is "the system could not say", never idle
    public void LonelyNeedsALongAbsenceAndNothingGoingOn(int minutes, bool media, bool gaming, bool locked, bool screenOn, bool expected) =>
        Assert.Equal(expected, MascotTriggers.Lonely(TimeSpan.FromMinutes(minutes), media, gaming, locked, screenOn));

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
