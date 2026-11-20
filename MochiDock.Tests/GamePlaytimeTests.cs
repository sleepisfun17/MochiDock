using MochiDock.Core;

public class GamePlaytimeTests
{
    static DateTime T(string s) => DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void ASessionIsSplitAtMidnight()
    {
        var ledger = new GamePlaytimeLedger();
        ledger.Add("Valorant", T("2026-11-13 23:30:00"), T("2026-11-14 00:45:00"));
        Assert.Equal(30 * 60, ledger.Today(T("2026-11-13 23:59:00")), 0.01);
        Assert.Equal(45 * 60, ledger.Today(T("2026-11-14 12:00:00")), 0.01);
        Assert.Equal(75 * 60, ledger.TotalBetween(T("2026-11-13"), T("2026-11-14")), 0.01);
    }

    [Fact]
    public void SessionsOfTheSameGameAddUpAndOrderByTimePlayed()
    {
        var ledger = new GamePlaytimeLedger();
        ledger.Add("PUBG", T("2026-11-14 10:00:00"), T("2026-11-14 11:00:00"));
        ledger.Add("valorant", T("2026-11-14 12:00:00"), T("2026-11-14 12:30:00"));
        ledger.Add("Valorant", T("2026-11-14 13:00:00"), T("2026-11-14 14:00:00"));    // same game, other case
        var top = ledger.Between(T("2026-11-14"), T("2026-11-14"));
        Assert.Equal(2, top.Count);
        Assert.Equal("valorant", top[0].Game, ignoreCase: true);
        Assert.Equal(90 * 60, top[0].Seconds, 0.01);
        Assert.Equal("PUBG", top[1].Game);
    }

    [Theory]
    [InlineData("2026-11-14", true, "2026-11-09")]    // a Saturday, week starts on Monday
    [InlineData("2026-11-14", false, "2026-11-08")]   // week starts on Sunday
    [InlineData("2026-11-15", true, "2026-11-09")]    // Sunday belongs to the week that started on Monday
    [InlineData("2026-11-15", false, "2026-11-15")]
    [InlineData("2026-11-09", true, "2026-11-09")]
    public void WeekStartFollowsTheFirstDaySetting(string now, bool monday, string expected) =>
        Assert.Equal(T(expected), GamePlaytimeLedger.WeekStart(T(now), monday));

    [Fact]
    public void ThisWeekCountsFromTheWeekStartOnly()
    {
        var ledger = new GamePlaytimeLedger();
        ledger.Add("G", T("2026-11-08 20:00:00"), T("2026-11-08 21:00:00"));   // Sunday, before the Monday-first week
        ledger.Add("G", T("2026-11-10 20:00:00"), T("2026-11-10 22:00:00"));
        ledger.Add("G", T("2026-11-14 20:00:00"), T("2026-11-14 20:30:00"));
        Assert.Equal(150 * 60, ledger.ThisWeek(T("2026-11-14 22:00:00"), true), 0.01);
        Assert.Equal(210 * 60, ledger.ThisWeek(T("2026-11-14 22:00:00"), false), 0.01);   // Sunday-first week includes the 8th
    }

    [Fact]
    public void PruneKeepsNinetyDaysAndDropsEmptyGames()
    {
        var ledger = new GamePlaytimeLedger();
        var now = T("2026-11-14 12:00:00");
        ledger.Add("Old", now.AddDays(-91), now.AddDays(-91).AddHours(1));
        ledger.Add("Edge", now.AddDays(-89), now.AddDays(-89).AddHours(1));
        ledger.Add("New", now.AddHours(-1), now);
        ledger.Prune(now);
        Assert.False(ledger.Games.ContainsKey("Old"));
        Assert.True(ledger.Games.ContainsKey("Edge"));
        Assert.True(ledger.Games.ContainsKey("New"));
    }

    [Fact]
    public void NamesAreTrimmedCappedAndNeverEmpty()
    {
        Assert.Equal("Game", GamePlaytimeLedger.NormalizeName("  "));
        Assert.Equal("Game", GamePlaytimeLedger.NormalizeName(null));
        Assert.Equal("Valorant", GamePlaytimeLedger.NormalizeName(" Valorant "));
        Assert.Equal(GamePlaytimeLedger.MaxGameNameLength, GamePlaytimeLedger.NormalizeName(new string('x', 200)).Length);
        var ledger = new GamePlaytimeLedger();
        ledger.Add("G", T("2026-11-14 10:00:00"), T("2026-11-14 10:00:00"));      // nothing played
        ledger.Add("G", T("2026-11-14 11:00:00"), T("2026-11-14 10:00:00"));      // end before start
        Assert.Empty(ledger.Games);
    }

    [Fact]
    public void LedgerRoundTripsThroughJson()
    {
        var ledger = new GamePlaytimeLedger();
        ledger.Add("Valorant", T("2026-11-14 10:00:00"), T("2026-11-14 11:00:00"));
        var back = System.Text.Json.JsonSerializer.Deserialize<GamePlaytimeLedger>(System.Text.Json.JsonSerializer.Serialize(ledger))!;
        Assert.Equal(3600, back.Today(T("2026-11-14")), 0.01);
        Assert.True(back.Games.ContainsKey("valorant"));       // names stay case-insensitive after loading
    }

    [Theory]
    [InlineData(0, 90, null, false)]       // off
    [InlineData(60, 0.5, null, false)]     // under a minute into the game
    [InlineData(60, 59, null, false)]      // not yet an hour
    [InlineData(60, 60, null, true)]       // an hour in
    [InlineData(60, 90, 60.0, false)]      // reminded at 60, next one at 120
    [InlineData(60, 120, 60.0, true)]
    [InlineData(90, 91, null, true)]
    [InlineData(45, 300, null, false)]     // an interval the settings do not offer counts as off
    public void BreakReminderTiming(int interval, double minutesIn, double? lastAtMinutes, bool due)
    {
        var start = T("2026-11-14 20:00:00");
        DateTime? last = lastAtMinutes is { } m ? start.AddMinutes(m) : null;
        Assert.Equal(due, BreakReminder.Due(start.AddMinutes(minutesIn), start, last, interval));
    }

    [Fact]
    public void AReminderFromAnEarlierSessionDoesNotCount()
    {
        var start = T("2026-11-14 20:00:00");
        Assert.False(BreakReminder.Due(start.AddMinutes(30), start, start.AddHours(-5), 60));   // last reminder was before this session
        Assert.True(BreakReminder.Due(start.AddMinutes(61), start, start.AddHours(-5), 60));
    }
}
