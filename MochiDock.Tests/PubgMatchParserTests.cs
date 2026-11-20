using MochiDock.Core;

public class PubgMatchParserTests
{
    // Shaped like the documented PUBG API answers (JSON:API), trimmed to the fields the parser reads.
    const string Players = """
    { "data": [ { "type": "player", "id": "account.abc123", "attributes": { "name": "Player" },
        "relationships": { "matches": { "data": [ { "type": "match", "id": "match-newest" }, { "type": "match", "id": "match-older" } ] } } } ] }
    """;

    const string Match = """
    { "data": { "type": "match", "id": "match-newest",
        "attributes": { "createdAt": "2026-11-14T12:00:00Z", "duration": 1500, "gameMode": "squad-fpp", "mapName": "Baltic_Main", "matchType": "official" } },
      "included": [
        { "type": "participant", "id": "p1", "attributes": { "stats": { "name": "Someone", "playerId": "account.other", "kills": 9, "winPlace": 1, "damageDealt": 900.0, "timeSurvived": 1490.0 } } },
        { "type": "participant", "id": "p2", "attributes": { "stats": { "name": "Player", "playerId": "account.abc123", "kills": 3, "assists": 1, "DBNOs": 4, "headshotKills": 2, "damageDealt": 412.6, "winPlace": 4, "timeSurvived": 1320.5 } } },
        { "type": "roster", "id": "r1", "attributes": { "won": "false" } }
      ] }
    """;

    [Fact]
    public void ReadsThePlayerAndTheNewestMatchFirst()
    {
        var player = PubgMatchParser.ParsePlayer(Players)!;
        Assert.Equal("account.abc123", player.AccountId);
        Assert.Equal(new[] { "match-newest", "match-older" }, player.MatchIds);
    }

    [Fact]
    public void ReadsThePlayersNumbersNotSomeoneElses()
    {
        var match = PubgMatchParser.ParseMatch(Match, "account.abc123")!;
        var s = match.Summary;
        Assert.Equal((3, 4, 2), (s.Kills, s.Placement, s.Headshots));
        Assert.Equal(412.6, s.Damage, 0.01);
        Assert.Equal(1320.5, s.SurvivedSeconds, 0.01);
        Assert.Equal("Squad FPP", s.Mode);
        Assert.Equal("Erangel", s.Map);
        Assert.Equal("", s.Result);                                              // only a first place is a win
        Assert.Equal(new DateTime(2026, 11, 14, 12, 25, 0, DateTimeKind.Utc), match.EndedUtc);
        Assert.Equal("#4 · 3 kills · 413 damage · 2 headshots · 22m survived · Squad FPP · Erangel", GameMatchFormat.Line(s));
    }

    [Fact]
    public void AWinnerIsCalledOne()
    {
        var other = PubgMatchParser.ParseMatch(Match, "account.other")!;
        Assert.Equal("Win", other.Summary.Result);
        Assert.StartsWith("Winner winner · 9 kills", GameMatchFormat.Line(other.Summary));
    }

    [Fact]
    public void AMatchWithoutThePlayerGivesNull() => Assert.Null(PubgMatchParser.ParseMatch(Match, "account.nobody"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("{}")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"errors":[{"title":"Not Found"}]}""")]
    public void NotAPlayerAnswerGivesNull(string? json) => Assert.Null(PubgMatchParser.ParsePlayer(json));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{"data":{"type":"match"}}""")]
    [InlineData("""{"data":{"attributes":{}},"included":[1,null,{"type":"participant"}]}""")]
    public void NotAMatchAnswerGivesNull(string? json) => Assert.Null(PubgMatchParser.ParseMatch(json, "account.abc123"));

    [Theory]
    [InlineData("squad-fpp", "Squad FPP")]
    [InlineData("duo", "Duo")]
    [InlineData("solo-fpp", "Solo FPP")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ModeNames(string? mode, string expected) => Assert.Equal(expected, PubgMatchParser.ModeName(mode));

    [Theory]
    [InlineData("Baltic_Main", "Erangel")]
    [InlineData("Desert_Main", "Miramar")]
    [InlineData("Tiger_Main", "Taego")]
    [InlineData("Some_New_Main", "Some New")]
    [InlineData("", "")]
    public void MapNames(string map, string expected) => Assert.Equal(expected, PubgMatchParser.MapName(map));

    [Theory]
    [InlineData("PUBG: BATTLEGROUNDS", true)]
    [InlineData("TslGame", true)]
    [InlineData("VALORANT", false)]
    [InlineData("League of Legends", false)]
    public void RecognisesTheGame(string name, bool expected) => Assert.Equal(expected, PubgMatchParser.IsPubg(new[] { "", name }));

    [Fact]
    public void AMatchBelongsToTheSessionItEndedIn()
    {
        var start = new DateTime(2026, 11, 14, 11, 40, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 11, 14, 12, 30, 0, DateTimeKind.Utc);
        Assert.True(PubgMatchWindow.Contains(new DateTime(2026, 11, 14, 12, 25, 0, DateTimeKind.Utc), start, end));
        Assert.True(PubgMatchWindow.Contains(new DateTime(2026, 11, 14, 12, 38, 0, DateTimeKind.Utc), start, end));    // a little after the game closed
        Assert.False(PubgMatchWindow.Contains(new DateTime(2026, 11, 14, 11, 30, 0, DateTimeKind.Utc), start, end));   // an earlier session's match
        Assert.False(PubgMatchWindow.Contains(new DateTime(2026, 11, 14, 13, 0, 0, DateTimeKind.Utc), start, end));
        Assert.False(PubgMatchWindow.Contains(DateTime.MinValue, start, end));
    }
}
