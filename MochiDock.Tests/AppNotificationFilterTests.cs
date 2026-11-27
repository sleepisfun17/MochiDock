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
        Assert.Single(AppNotificationFilter.Add(list, new WatchedApp("MochiDock", "MochiDock.App")));
        Assert.Equal(2, AppNotificationFilter.Add(list, Telegram).Count);
    }

    [Fact]
    public void WatchAllMatchesOtherAppsExceptDiscordAndSelf()
    {
        var chatGpt = AppNotificationFilter.Match("ChatGPT", "OpenAI.Codex_2p2nqsd0c76g0!App", Array.Empty<WatchedApp>(), watchAll: true);
        Assert.NotNull(chatGpt);
        Assert.Equal("ChatGPT", chatGpt!.Name);
        Assert.Equal("OpenAI.Codex_2p2nqsd0c76g0!App", chatGpt.Aumid);

        var aumidOnly = AppNotificationFilter.Match("", "Slack.App", null, watchAll: true);
        Assert.NotNull(aumidOnly);
        Assert.Equal("Slack.App", aumidOnly!.Name);

        Assert.Null(AppNotificationFilter.Match("Discord", "com.squirrel.Discord.Discord", null, watchAll: true));
        Assert.Null(AppNotificationFilter.Match("MochiDock", "sleepisfun17.MochiDock", null, watchAll: true));
        Assert.Null(AppNotificationFilter.Match("MochiDock", "", new[] { new WatchedApp("MochiDock", "") }, watchAll: false));
        Assert.Null(AppNotificationFilter.Match(" ", " ", null, watchAll: true));
    }
}
