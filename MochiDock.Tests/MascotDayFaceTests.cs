using MochiDock.Core;

public class MascotDayFaceTests
{
    static readonly DateTime Noon = new(2026, 11, 21, 12, 0, 0);

    [Fact]
    public void MeetingThenBirthdayThenProudThenLateNight()
    {
        var night = new DateTime(2026, 11, 21, 23, 30, 0);
        Assert.Equal(MascotMood.Meeting, MascotTriggers.DayFace(night, "11-21", true, true));
        Assert.Equal(MascotMood.Birthday, MascotTriggers.DayFace(night, "11-21", true, false));
        Assert.Equal(MascotMood.Proud, MascotTriggers.DayFace(night, "", true, false));
        Assert.Equal(MascotMood.Yawning, MascotTriggers.DayFace(night, null, false, false));
        Assert.Null(MascotTriggers.DayFace(Noon, null, false, false));
    }

    [Theory]
    [InlineData(22, false)]
    [InlineData(23, true)]
    [InlineData(2, true)]
    [InlineData(5, false)]
    public void LateNightIsElevenUntilFive(int hour, bool late) =>
        Assert.Equal(late, MascotTriggers.IsLateNight(new DateTime(2026, 11, 21, hour, 0, 0)));

    [Fact]
    public void BirthdayReadsMonthDayAndKeepsLeapDays()
    {
        Assert.True(MascotTriggers.IsBirthday(Noon, "11-21"));
        Assert.True(MascotTriggers.IsBirthday(Noon, " 11-21 "));
        Assert.False(MascotTriggers.IsBirthday(Noon, "11-22"));
        Assert.False(MascotTriggers.IsBirthday(Noon, ""));
        Assert.False(MascotTriggers.IsBirthday(Noon, "13-01"));
        Assert.True(MascotTriggers.IsBirthday(new DateTime(2027, 2, 28), "02-29"));
        Assert.False(MascotTriggers.IsBirthday(new DateTime(2028, 2, 28), "02-29"));
        Assert.True(MascotTriggers.IsBirthday(new DateTime(2028, 2, 29), "02-29"));
        Assert.Null(MascotTriggers.ParseMonthDay("02-30"));
    }

    [Fact]
    public void AllTasksDoneNeedsTodaysTasksFinishedAndNothingOverdue()
    {
        var today = Noon.Date;
        Assert.True(MascotTriggers.AllTasksDone(new (DateTime?, bool)[] { (today.AddHours(9), true), (today.AddHours(15), true), (null, false) }, Noon));
        Assert.False(MascotTriggers.AllTasksDone(new (DateTime?, bool)[] { (today.AddHours(9), true), (today.AddHours(15), false) }, Noon));
        Assert.False(MascotTriggers.AllTasksDone(new (DateTime?, bool)[] { (today.AddHours(9), true), (today.AddDays(-1), false) }, Noon));
        Assert.False(MascotTriggers.AllTasksDone(new (DateTime?, bool)[] { (today.AddDays(1), true), (null, true) }, Noon));   // nothing due today
        Assert.True(MascotTriggers.AllTasksDone(new (DateTime?, bool)[] { (today.AddHours(9), true), (today.AddDays(1), false) }, Noon));
    }

    [Fact]
    public void TheDayFaceOnlyReplacesARestingFace()
    {
        MascotInputs With(MascotMood requested, MascotMood? activity = null) =>
            new(requested, true, false, false, false, false, MascotWeather.Hot, Activity: activity, Day: MascotMood.Birthday);
        Assert.Equal(MascotMood.Birthday, MascotMoodPolicy.Choose(With(MascotMood.Idle)));      // beats the weather
        Assert.Equal(MascotMood.Birthday, MascotMoodPolicy.Choose(With(MascotMood.Coffee)));
        Assert.Equal(MascotMood.Music, MascotMoodPolicy.Choose(With(MascotMood.Music)));        // music keeps its headphones
        Assert.Equal(MascotMood.Electric, MascotMoodPolicy.Choose(With(MascotMood.Idle, MascotMood.Electric)));   // the open card wins
        Assert.Equal(MascotMood.Excited, MascotMoodPolicy.Choose(With(MascotMood.Excited)));
        Assert.Equal(MascotMood.Sleeping, MascotMoodPolicy.Choose(With(MascotMood.Sleeping)));
        Assert.Equal(MascotMood.Happy, MascotMoodPolicy.Choose(With(MascotMood.Happy)));
    }
}
