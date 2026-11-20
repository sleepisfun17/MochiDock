using MochiDock.Core;

public class LolMatchParserTests
{
    // Shaped like the documented Live Client Data API answer, trimmed to the fields the parser reads.
    const string Match = """
    {
      "activePlayer": { "riotId": "Player#JP1", "riotIdGameName": "Player", "riotIdTagLine": "JP1", "summonerName": "Player#JP1", "level": 15, "currentGold": 1200.5 },
      "allPlayers": [
        { "championName": "Ahri", "isBot": false, "riotId": "Player#JP1", "riotIdGameName": "Player", "riotIdTagLine": "JP1", "summonerName": "Player#JP1", "team": "ORDER",
          "scores": { "assists": 12, "creepScore": 187, "deaths": 3, "kills": 8, "wardScore": 14.0 } },
        { "championName": "Zed", "isBot": false, "riotId": "Enemy#EUW", "summonerName": "Enemy#EUW", "team": "CHAOS",
          "scores": { "assists": 1, "creepScore": 200, "deaths": 9, "kills": 11, "wardScore": 3.0 } }
      ],
      "events": { "Events": [
        { "EventID": 0, "EventName": "GameStart", "EventTime": 0.05 },
        { "EventID": 5, "EventName": "ChampionKill", "KillerName": "Player#JP1", "VictimName": "Enemy#EUW", "Assisters": [], "EventTime": 400.0 },
        { "EventID": 6, "EventName": "Multikill", "KillerName": "Enemy#EUW", "KillStreak": 4, "EventTime": 900.0 },
        { "EventID": 7, "EventName": "Multikill", "KillerName": "Player", "KillStreak": 2, "EventTime": 1000.0 },
        { "EventID": 8, "EventName": "Multikill", "KillerName": "Player#JP1", "KillStreak": 3, "EventTime": 1500.0 },
        { "EventID": 9, "EventName": "GameEnd", "Result": "Win", "EventTime": 1800.0 }
      ] },
      "gameData": { "gameMode": "CLASSIC", "gameTime": 1805.5, "mapName": "Map11" }
    }
    """;

    [Fact]
    public void ReadsTheActivePlayersNumbers()
    {
        var m = LolMatchParser.TryParse(Match)!;
        Assert.Equal("League of Legends", m.Game);
        Assert.Equal("Ahri", m.Champion);
        Assert.Equal((8, 3, 12), (m.Kills, m.Deaths, m.Assists));
        Assert.Equal(187, m.CreepScore);
        Assert.Equal("Win", m.Result);
        Assert.Equal(3, m.BestMultikill);              // the enemy's quadra kill does not count; the player's best is the triple
        Assert.Equal(1805.5, m.GameSeconds, 0.01);
        Assert.Equal("CLASSIC", m.Mode);
    }

    [Fact]
    public void FormatLeavesOutWhatIsUnknown()
    {
        Assert.Equal("Win · Ahri · 8/3/12 · CS 187 · Triple kill", GameMatchFormat.Line(LolMatchParser.TryParse(Match)));
        Assert.Equal("5/1/2", GameMatchFormat.Line(new GameMatchSummary { Kills = 5, Deaths = 1, Assists = 2 }));
        Assert.Equal("Loss · 0/7/1 · ARAM", GameMatchFormat.Line(new GameMatchSummary { Result = "Loss", Deaths = 7, Assists = 1, Mode = "ARAM" }));
        Assert.Equal("", GameMatchFormat.Line(null));
    }

    [Fact]
    public void AMatchWithoutAnEndEventHasNoResult()
    {
        string running = Match.Replace("""{ "EventID": 9, "EventName": "GameEnd", "Result": "Win", "EventTime": 1800.0 }""", """{ "EventID": 9, "EventName": "TurretKilled", "EventTime": 1800.0 }""");
        Assert.Equal("", LolMatchParser.TryParse(running)!.Result);
    }

    [Theory]
    [InlineData("Lose", "Loss")]
    [InlineData("Win", "Win")]
    [InlineData("win", "Win")]
    public void ResultWordsAreNormalised(string given, string expected) =>
        Assert.Equal(expected, LolMatchParser.TryParse(Match.Replace("\"Result\": \"Win\"", $"\"Result\": \"{given}\""))!.Result);

    [Fact]
    public void WorksWithTheOldSummonerNameOnly()
    {
        string old = """
        { "activePlayer": { "summonerName": "OldName" },
          "allPlayers": [ { "championName": "Lux", "summonerName": "OldName", "scores": { "kills": 1, "deaths": 2, "assists": 3, "creepScore": 40 } } ] }
        """;
        var m = LolMatchParser.TryParse(old)!;
        Assert.Equal("Lux", m.Champion);
        Assert.Equal(40, m.CreepScore);
        Assert.Equal("", m.Result);
        Assert.Equal(0, m.BestMultikill);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"httpStatus":404,"errorCode":"RESOURCE_NOT_FOUND"}""")]
    [InlineData("""{"activePlayer":{},"allPlayers":[]}""")]
    [InlineData("""{"activePlayer":{"riotId":"A#1"},"allPlayers":[{"riotId":"B#2","scores":{}}]}""")]   // the active player is not in the list (spectating)
    public void NotAMatchOfAPlayerGivesNull(string? json) => Assert.Null(LolMatchParser.TryParse(json));

    [Fact]
    public void OddFieldTypesDoNotThrow()
    {
        string odd = """
        { "activePlayer": { "riotId": "A#1" },
          "allPlayers": [ { "championName": 5, "riotId": "A#1", "scores": { "kills": "many", "deaths": 2.5, "assists": null } } ],
          "events": { "Events": [ 3, null, { "EventName": "Multikill", "KillerName": "A#1", "KillStreak": 99 } ] },
          "gameData": { "gameMode": 1, "gameTime": "soon" } }
        """;
        var m = LolMatchParser.TryParse(odd)!;
        Assert.Equal(0, m.Kills);
        Assert.Equal(5, m.BestMultikill);             // clamped to a penta kill
        Assert.Equal("", m.Champion);
    }

    [Theory]
    [InlineData("League of Legends", true)]
    [InlineData("League of Legends (TM) Client", true)]
    [InlineData("leagueclientux", false)]
    [InlineData("VALORANT", false)]
    public void RecognisesTheGameButNotTheLauncher(string name, bool expected) =>
        Assert.Equal(expected, LolMatchParser.IsLeague(new[] { "", name }));
}
