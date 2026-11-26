using MochiDock.Core;

public class MascotTriggersTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    static AlertReading Reading(double value, double ageSeconds = 0, bool measured = true) => new(value, "NVML", Now.AddSeconds(-ageSeconds), measured);

    [Theory]
    [InlineData(true, false, 15, false, true)]    // at 15% on battery
    [InlineData(true, false, 16, false, false)]   // not low yet
    [InlineData(true, false, 18, true, true)]     // stays low until 20%
    [InlineData(true, false, 20, true, false)]
    [InlineData(true, true, 5, true, false)]      // plugged in: never drained
    [InlineData(false, false, 5, true, false)]    // no battery
    [InlineData(true, false, -1, false, false)]   // unknown level
    [InlineData(true, false, 101, false, false)]
    public void BatteryLowHasAGapAndNeedsUnplugged(bool has, bool ac, int percent, bool wasLow, bool expected) =>
        Assert.Equal(expected, MascotTriggers.BatteryLow(new PowerState(has, ac, percent), wasLow));

    [Fact]
    public void GpuHotNeedsAFreshMeasuredReadingAtTheThreshold()
    {
        Assert.True(MascotTriggers.GpuHot(Reading(85), 85, false, Now));
        Assert.False(MascotTriggers.GpuHot(Reading(84), 85, false, Now));
        Assert.False(MascotTriggers.GpuHot(Reading(95, ageSeconds: 6), 85, true, Now));      // stale
        Assert.False(MascotTriggers.GpuHot(Reading(95, measured: false), 85, false, Now));  // an estimate never counts
        Assert.False(MascotTriggers.GpuHot(null, 85, true, Now));
        Assert.False(MascotTriggers.GpuHot(Reading(double.NaN), 85, false, Now));
        Assert.False(MascotTriggers.GpuHot(new AlertReading(95, "NVML", Now.AddSeconds(5), true), 85, false, Now));   // from the future
    }

    [Fact]
    public void GpuHotClearsThreeDegreesBelowTheThreshold()
    {
        Assert.True(MascotTriggers.GpuHot(Reading(83), 85, currentlyHot: true, Now));
        Assert.True(MascotTriggers.GpuHot(Reading(82), 85, currentlyHot: true, Now));
        Assert.False(MascotTriggers.GpuHot(Reading(81.9), 85, currentlyHot: true, Now));
        Assert.False(MascotTriggers.GpuHot(Reading(83), 85, currentlyHot: false, Now));
    }

    [Fact]
    public void MeasuredLoadIsNaNUnlessMeasuredAndFresh()
    {
        Assert.Equal(97, MascotTriggers.MeasuredLoad(Reading(97), Now));
        Assert.True(double.IsNaN(MascotTriggers.MeasuredLoad(Reading(97, ageSeconds: 9), Now)));
        Assert.True(double.IsNaN(MascotTriggers.MeasuredLoad(Reading(97, measured: false), Now)));
        Assert.True(double.IsNaN(MascotTriggers.MeasuredLoad(null, Now)));
    }

    static readonly DateTime Due = new(2026, 11, 14, 14, 30, 0);

    [Theory]
    [InlineData(0.5, false)]    // not yet 1 minute late
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(45, true)]
    [InlineData(89, true)]
    [InlineData(90, false)]     // Mochi gives up
    [InlineData(-5, false)]
    public void AnOverdueTaskMakesMochiAngryBetweenOneAndNinetyMinutes(double minutesLate, bool expected) =>
        Assert.Equal(expected, MascotTriggers.UnacknowledgedOverdue(new[] { new ReminderTask("t", "Send report", Due, false) }, Due.AddMinutes(minutesLate), new HashSet<string>()));

    [Fact]
    public void AcknowledgedDoneAndUntimedTasksNeverMakeMochiAngry()
    {
        var now = Due.AddMinutes(30);
        var acknowledged = new HashSet<string> { AgendaReminderPolicy.TaskItemKey("t", Due) };
        Assert.False(MascotTriggers.UnacknowledgedOverdue(new[] { new ReminderTask("t", "x", Due, false) }, now, acknowledged));
        Assert.False(MascotTriggers.UnacknowledgedOverdue(new[] { new ReminderTask("t", "x", Due, true) }, now, new HashSet<string>()));
        Assert.False(MascotTriggers.UnacknowledgedOverdue(new[] { new ReminderTask("t", "x", null, false) }, now, new HashSet<string>()));
        Assert.True(MascotTriggers.UnacknowledgedOverdue(new[] { new ReminderTask("t", "x", Due, true), new ReminderTask("u", "y", Due, false) }, now, acknowledged));
    }

    [Fact]
    public void TheTaskItemKeyMatchesWhatTheReminderStoresForAcknowledging()
    {
        var reminder = AgendaReminderPolicy.Due(Array.Empty<AgendaEvent>(), new[] { new ReminderTask("t", "x", Due, false) }, Due.AddMinutes(-3), 10, new HashSet<string>())[0];
        Assert.Equal(AgendaReminderPolicy.TaskItemKey("t", Due), reminder.ItemKey);
    }

    static GameMatchSummary Match(string result = "", int placement = 0) => new() { Result = result, Placement = placement };

    [Theory]
    [InlineData("Win", 0, MascotMood.Victory)]
    [InlineData("Loss", 0, MascotMood.Defeat)]
    [InlineData("", 1, MascotMood.Victory)]       // first place in a battle royale
    [InlineData("", 11, MascotMood.Defeat)]       // outside the top ten
    public void AMatchResultChoosesVictoryOrDefeat(string result, int placement, MascotMood expected) =>
        Assert.Equal(expected, MascotTriggers.MatchMood(Match(result, placement)));

    [Theory]
    [InlineData("", 0)]     // the game reported no end
    [InlineData("", 2)]     // the top ten is neither
    [InlineData("", 10)]
    public void AnUndecidedMatchHasNoFace(string result, int placement) => Assert.Null(MascotTriggers.MatchMood(Match(result, placement)));

    [Fact]
    public void NoMatchHasNoFace() => Assert.Null(MascotTriggers.MatchMood(null));
}
