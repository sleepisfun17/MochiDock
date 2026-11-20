using MochiDock.Core;

public class GameProfilesTests
{
    static GameProfile P(string game, string res = "", string click = "Default", string notes = "Default") =>
        new() { Game = game, Resolution = res, ClickThrough = click, Notifications = notes };

    [Theory]
    [InlineData("VALORANT-Win64-Shipping.exe", "valorant-win64-shipping")]
    [InlineData("  Valorant  ", "valorant")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void KeyIgnoresCaseSpacesAndExe(string? name, string expected) => Assert.Equal(expected, GameProfiles.Key(name));

    [Fact]
    public void FindMatchesAnyOfTheNamesBeingPlayed()
    {
        var profiles = new[] { P("VALORANT-Win64-Shipping", "2304x1440"), P("League of Legends") };
        Assert.Equal("VALORANT-Win64-Shipping", GameProfiles.Find(profiles, new[] { "VALORANT", "valorant-win64-shipping.exe" })!.Game);
        Assert.Equal("League of Legends", GameProfiles.Find(profiles, new[] { "", "league of legends" })!.Game);
        Assert.Null(GameProfiles.Find(profiles, new[] { "PUBG" }));
        Assert.Null(GameProfiles.Find(profiles, Array.Empty<string>()));
        Assert.Null(GameProfiles.Find(null, new[] { "x" }));
        Assert.Null(GameProfiles.Find(new[] { P("") }, new[] { "" }));        // a profile without a game never matches
    }

    [Theory]
    [InlineData(true, "Default", true)]
    [InlineData(false, "Default", false)]
    [InlineData(false, "On", true)]
    [InlineData(true, "Off", false)]
    [InlineData(true, "nonsense", true)]
    public void ClickThroughFollowsTheProfileOrTheGlobalSetting(bool global, string profile, bool expected) =>
        Assert.Equal(expected, GameProfiles.ClickThroughEnabled(global, P("G", click: profile)));

    [Theory]
    [InlineData(true, "Default", true)]
    [InlineData(false, "Default", false)]
    [InlineData(false, "Hold", true)]
    [InlineData(true, "Allow", false)]
    public void NotificationsFollowTheProfileOrTheGlobalSetting(bool global, string profile, bool expected) =>
        Assert.Equal(expected, GameProfiles.HoldNotifications(global, P("G", notes: profile)));

    [Fact]
    public void NoProfileMeansGlobalSettings()
    {
        Assert.True(GameProfiles.ClickThroughEnabled(true, null));
        Assert.False(GameProfiles.HoldNotifications(false, null));
    }

    [Fact]
    public void ResolutionIsOnlyAppliedWhenItDiffersAndParses()
    {
        Assert.Equal((2304, 1440), GameProfiles.ResolutionToApply(P("G", "2304x1440"), 2560, 1440));
        Assert.Null(GameProfiles.ResolutionToApply(P("G", "2304x1440"), 2304, 1440));    // already there
        Assert.Null(GameProfiles.ResolutionToApply(P("G", ""), 2560, 1440));              // no change wanted
        Assert.Null(GameProfiles.ResolutionToApply(P("G", "wide"), 2560, 1440));          // unreadable
        Assert.Null(GameProfiles.ResolutionToApply(null, 2560, 1440));
    }

    [Fact]
    public void ResolutionIsRestoredOnlyWhileTheProfileResolutionIsStillSet()
    {
        Assert.Equal((2560, 1440), GameProfiles.ResolutionToRestore((2560, 1440), (2304, 1440), 2304, 1440));
        Assert.Null(GameProfiles.ResolutionToRestore((2560, 1440), (2304, 1440), 1920, 1080));   // you changed it yourself
        Assert.Null(GameProfiles.ResolutionToRestore((2304, 1440), (2304, 1440), 2304, 1440));   // nothing was switched
    }

    [Fact]
    public void UpsertReplacesTheProfileOfTheSameGame()
    {
        var list = new List<GameProfile> { P("Valorant", "1920x1080"), P("PUBG") };
        var updated = GameProfiles.Upsert(list, P("VALORANT.exe", "2304x1440"));
        Assert.Equal(2, updated.Count);
        Assert.Equal("2304x1440", updated.Single(p => GameProfiles.Key(p.Game) == "valorant").Resolution);
    }
}
