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

    [Theory]
    [InlineData("Catch up", "", "https://meet.google.com/abc-defg-hij", false, true)]
    [InlineData("Weekly sync", "", null, false, true)]
    [InlineData("Team Stand-up", "", null, false, true)]
    [InlineData("1:1 with manager", "", null, false, true)]
    [InlineData("Client call", "", null, false, true)]
    [InlineData("Sprint planning", "", null, false, true)]
    [InlineData("Candidate interview", "", null, false, true)]
    [InlineData("定例", "", null, false, true)]
    [InlineData("案件MTG", "", null, false, true)]
    [InlineData("打ち合わせ", "", null, false, true)]
    [InlineData("採用面談", "", null, false, true)]
    [InlineData("Rapat mingguan", "", null, false, true)]
    [InlineData("Diskusi proyek", "", null, false, true)]
    [InlineData("Project catchup", "Zoom", null, false, true)]
    [InlineData("Project catchup", "会議室A", null, false, true)]
    [InlineData("Project catchup", "Ruang Rapat 2", null, false, true)]
    [InlineData("Lunch", "", null, false, false)]
    [InlineData("Gym", "", null, false, false)]
    [InlineData("Dentist", "", null, false, false)]
    [InlineData("Work", "", null, false, false)]
    [InlineData("Focus block", "", null, false, false)]
    [InlineData("On-call", "", null, false, false)]
    [InlineData("On call shift", "", null, false, false)]
    [InlineData("Hotel check-in", "", null, false, false)]
    [InlineData("Weekly meeting", "", "https://meet.google.com/abc-defg-hij", true, false)]
    public void IsMeetingMatchesLinksAndMeetingKeywordsOnly(string title, string place, string? joinUrl, bool allDay, bool expected)
    {
        var ev = new AgendaEvent("1", title, Noon.AddHours(-1), Noon.AddHours(1), allDay, place, joinUrl);
        Assert.Equal(expected, AgendaLogic.IsMeeting(ev));
    }

    [Fact]
    public void HasActiveMeetingIgnoresNonMeetingBlocksAndFindsOverlappingMeeting()
    {
        var workBlock = new AgendaEvent("w", "Work", Noon.AddHours(-3), Noon.AddHours(6), false, "", null);
        var lunch = new AgendaEvent("l", "Lunch", Noon, Noon.AddHours(1), false, "", null);
        var sync = new AgendaEvent("m", "Team Sync", Noon.AddHours(-2), Noon.AddMinutes(-90), false, "", null);
        var events = new[] { workBlock, lunch, sync };

        Assert.True(AgendaLogic.HasActiveMeeting(events, Noon.AddMinutes(-105)));
        Assert.False(AgendaLogic.HasActiveMeeting(events, Noon.AddMinutes(-60)));
        Assert.False(AgendaLogic.HasActiveMeeting(events, Noon.AddMinutes(15)));
    }

    [Theory]
    [InlineData("Leg day at the gym", "", null, false, CalendarActivityKind.Workout)]
    [InlineData("Morning Work out", "", null, false, CalendarActivityKind.Workout)]
    [InlineData("筋トレ", "", null, false, CalendarActivityKind.Workout)]
    [InlineData("Olahraga sore", "", null, false, CalendarActivityKind.Workout)]
    [InlineData("Evening stretch", "Anytime Fitness", null, false, CalendarActivityKind.Workout)]
    [InlineData("Team lunch", "", null, false, CalendarActivityKind.Eating)]
    [InlineData("Dinner with family", "", null, false, CalendarActivityKind.Eating)]
    [InlineData("昼ご飯", "", null, false, CalendarActivityKind.Eating)]
    [InlineData("Makan siang", "", null, false, CalendarActivityKind.Eating)]
    [InlineData("Work", "", null, false, CalendarActivityKind.Working)]
    [InlineData("Deep work block", "", null, false, CalendarActivityKind.Working)]
    [InlineData("勤務", "", null, false, CalendarActivityKind.Working)]
    [InlineData("Kerja di kantor", "", null, false, CalendarActivityKind.Working)]
    [InlineData("Working lunch", "", null, false, CalendarActivityKind.Eating)]
    [InlineData("Team sync", "", "https://meet.google.com/abc-defg-hij", false, CalendarActivityKind.Meeting)]
    [InlineData("Dentist", "", null, false, CalendarActivityKind.None)]
    [InlineData("On-call", "", null, false, CalendarActivityKind.None)]
    [InlineData("Gym day", "", null, true, CalendarActivityKind.None)]
    public void ClassifyActivityDetectsWorkoutEatingWorkingAndMeeting(string title, string place, string? joinUrl, bool allDay, CalendarActivityKind expected)
    {
        var ev = new AgendaEvent("1", title, Noon.AddHours(-1), Noon.AddHours(1), allDay, place, joinUrl);
        Assert.Equal(expected, AgendaLogic.ClassifyActivity(ev));
    }

    [Fact]
    public void ActiveCalendarActivityUsesPriorityAcrossOverlappingEvents()
    {
        var work = new AgendaEvent("w", "Work", Noon.AddHours(-3), Noon.AddHours(6), false, "", null);
        var gym = new AgendaEvent("g", "Gym", Noon.AddMinutes(-20), Noon.AddMinutes(40), false, "", null);
        var lunch = new AgendaEvent("l", "Lunch", Noon.AddMinutes(-10), Noon.AddMinutes(30), false, "", null);
        var sync = new AgendaEvent("m", "Team Sync", Noon, Noon.AddMinutes(20), false, "", null);

        Assert.Equal(CalendarActivityKind.Working, AgendaLogic.ActiveCalendarActivity(new[] { work }, Noon.AddHours(-1)));
        Assert.Equal(CalendarActivityKind.Workout, AgendaLogic.ActiveCalendarActivity(new[] { work, gym }, Noon.AddMinutes(-15)));
        Assert.Equal(CalendarActivityKind.Eating, AgendaLogic.ActiveCalendarActivity(new[] { work, gym, lunch }, Noon.AddMinutes(-5)));
        Assert.Equal(CalendarActivityKind.Meeting, AgendaLogic.ActiveCalendarActivity(new[] { work, gym, lunch, sync }, Noon.AddMinutes(10)));
    }

    [Fact]
    public void DayFaceOrdersCalendarActivitiesAfterBirthdayAndBeforeProud()
    {
        var night = new DateTime(2026, 11, 21, 23, 30, 0);
        Assert.Equal(MascotMood.Meeting, MascotTriggers.DayFace(night, "11-21", true, CalendarActivityKind.Meeting));
        Assert.Equal(MascotMood.Birthday, MascotTriggers.DayFace(night, "11-21", true, CalendarActivityKind.Eating));
        Assert.Equal(MascotMood.Eating, MascotTriggers.DayFace(night, "", true, CalendarActivityKind.Eating));
        Assert.Equal(MascotMood.Workout, MascotTriggers.DayFace(night, "", true, CalendarActivityKind.Workout));
        Assert.Equal(MascotMood.Working, MascotTriggers.DayFace(night, "", true, CalendarActivityKind.Working));
        Assert.Equal(MascotMood.Coffee, MascotTriggers.DayFace(night, "", true, CalendarActivityKind.Working, onWorkBreak: true));
        Assert.Equal(MascotMood.Proud, MascotTriggers.DayFace(night, "", true, CalendarActivityKind.None));
    }

    [Fact]
    public void CalendarActivityRulesSupportTitleOverridesAndCalendarDefaults()
    {
        var rules = CalendarActivityRules.Parse(
            new Dictionary<string, string>
            {
                ["project alpha"] = "Working",
                ["silent sync"] = "None",
            },
            new Dictionary<string, string>
            {
                ["Work Cal"] = "Working",
                ["Muted Cal"] = "None",
            });

        // Generic title on Work Cal falls back to calendar default Working.
        var projectAlpha = new AgendaEvent("1", "Project Alpha", Noon, Noon.AddHours(1), false, "", null, CalendarName: "Work Cal");
        var genericWork = new AgendaEvent("2", "Daily planning", Noon, Noon.AddHours(1), false, "", null, CalendarName: "Work Cal");
        Assert.Equal(CalendarActivityKind.Working, AgendaLogic.ClassifyActivity(projectAlpha, rules));
        Assert.Equal(CalendarActivityKind.Working, AgendaLogic.ClassifyActivity(genericWork, rules));

        // Explicit auto-detected Meeting or Eating on Work Cal still beats the calendar default.
        var syncOnWork = new AgendaEvent("3", "Team Sync", Noon, Noon.AddHours(1), false, "", null, CalendarName: "Work Cal");
        var lunchOnWork = new AgendaEvent("4", "Lunch", Noon, Noon.AddHours(1), false, "", null, CalendarName: "Work Cal");
        Assert.Equal(CalendarActivityKind.Meeting, AgendaLogic.ClassifyActivity(syncOnWork, rules));
        Assert.Equal(CalendarActivityKind.Eating, AgendaLogic.ClassifyActivity(lunchOnWork, rules));

        // Explicit title "None" suppresses an auto-detected Meeting.
        var silentSync = new AgendaEvent("5", "Silent Sync", Noon, Noon.AddHours(1), false, "", "https://meet.google.com/aaa-bbbb-ccc", CalendarName: "Work Cal");
        Assert.Equal(CalendarActivityKind.None, AgendaLogic.ClassifyActivity(silentSync, rules));

        // Calendar default "None" silences events on that calendar unless overridden by title.
        var mutedLunch = new AgendaEvent("6", "Lunch", Noon, Noon.AddHours(1), false, "", null, CalendarName: "Muted Cal");
        var overriddenAlpha = new AgendaEvent("7", "Project Alpha", Noon, Noon.AddHours(1), false, "", null, CalendarName: "Muted Cal");
        Assert.Equal(CalendarActivityKind.None, AgendaLogic.ClassifyActivity(mutedLunch, rules));
        Assert.Equal(CalendarActivityKind.Working, AgendaLogic.ClassifyActivity(overriddenAlpha, rules));
    }

    [Fact]
    public void WorkBreakTrackerTriggersCoffeeAt45MinFor5MinAndResetsWhenIdle()
    {
        var tracker = new WorkBreakTracker();
        var t0 = Noon;

        Assert.False(tracker.Update(t0, isWorking: true, TimeSpan.Zero, awayOrLocked: false));
        Assert.False(tracker.Update(t0.AddMinutes(44), isWorking: true, TimeSpan.FromSeconds(10), awayOrLocked: false));

        // Reaches 45 minutes -> enters 5-minute Coffee break.
        Assert.True(tracker.Update(t0.AddMinutes(45), isWorking: true, TimeSpan.Zero, awayOrLocked: false));
        Assert.True(tracker.Update(t0.AddMinutes(49), isWorking: true, TimeSpan.Zero, awayOrLocked: false));

        // At 50 minutes -> returns to Working and starts a fresh 45-minute cycle.
        Assert.False(tracker.Update(t0.AddMinutes(50), isWorking: true, TimeSpan.Zero, awayOrLocked: false));
        Assert.False(tracker.Update(t0.AddMinutes(80), isWorking: true, TimeSpan.Zero, awayOrLocked: false));

        // 3 minutes of natural idle resets the 45-minute clock.
        Assert.False(tracker.Update(t0.AddMinutes(85), isWorking: true, TimeSpan.FromMinutes(3), awayOrLocked: false));
        Assert.False(tracker.Update(t0.AddMinutes(120), isWorking: true, TimeSpan.Zero, awayOrLocked: false));
    }
}
