using MochiDock.Core;

public class AppNotificationFilterTests
{
    static readonly WatchedApp Line = new("LINE", "NAVER.LINEWin8_8ptj331gd3tyt!LINE");
    static readonly WatchedApp Telegram = new("Telegram Desktop", "");

    [Fact]
    public void AnAumidEntryMatchesOnlyThatAumid()
    {
        Assert.Equal(Line, AppNotificationFilter.Match("Something", "naver.linewin8_8ptj331gd3tyt!line", new[] { Line }));
        Assert.Null(AppNotificationFilter.Match("LINE", "Other.App!LINE", new[] { Line }));
    }

    [Fact]
    public void ANameEntryMatchesTheDisplayNameIgnoringCaseAndSpaces()
    {
        Assert.Equal(Telegram, AppNotificationFilter.Match(" telegram desktop ", "Telegram.TelegramDesktop", new[] { Telegram }));
        Assert.Null(AppNotificationFilter.Match("Telegram", "", new[] { Telegram }));
    }

    [Fact]
    public void DiscordAndEmptyListsNeverMatch()
    {
        var discord = new WatchedApp("Discord", "");
        Assert.Null(AppNotificationFilter.Match("Discord", "com.squirrel.Discord.Discord", new[] { discord }));
        Assert.Null(AppNotificationFilter.Match("LINE", Line.Aumid, Array.Empty<WatchedApp>()));
        Assert.Null(AppNotificationFilter.Match("LINE", Line.Aumid, null));
        Assert.Empty(AppNotificationFilter.Add(null, discord));
    }

    [Fact]
    public void SplitUsesTheFirstLineAsTitle()
    {
        Assert.Equal(("Aki", "See you\nat 7"), AppNotificationFilter.Split(new[] { " Aki ", "", "See you", "at 7" }, "LINE"));
        Assert.Equal(("LINE", ""), AppNotificationFilter.Split(new string?[] { null, " " }, "LINE"));
    }

    [Fact]
    public void RememberMovesToFrontWithoutDuplicatesAndCaps()
    {
        var recent = Enumerable.Range(0, 25).Select(i => new WatchedApp($"App {i}", $"id{i}")).ToList();
        var result = AppNotificationFilter.Remember(recent, new WatchedApp("App 7 renamed", "ID7"));
        Assert.Equal(AppNotificationFilter.RecentCap, result.Count);
        Assert.Equal("App 7 renamed", result[0].Name);
        Assert.Single(result, r => r.Aumid.Equals("id7", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddSkipsDuplicatesAndBlanks()
    {
        var list = AppNotificationFilter.Add(new[] { Line }, new WatchedApp("LINE again", Line.Aumid.ToUpperInvariant()));
        Assert.Single(list);
        Assert.Single(AppNotificationFilter.Add(list, new WatchedApp(" ", "")));
        Assert.Equal(2, AppNotificationFilter.Add(list, Telegram).Count);
    }
}
