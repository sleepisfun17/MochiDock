using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace MochiDock.Core;

/// <summary>What a game's own API says about one match: result, kills/deaths/assists and similar numbers. Everything is optional.</summary>
public sealed class GameMatchSummary
{
    public string Game { get; set; } = "";
    /// <summary>"Win", "Loss" or "" when the game did not report an end (for example it closed first).</summary>
    public string Result { get; set; } = "";
    public string Champion { get; set; } = "";
    public string Mode { get; set; } = "";
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int CreepScore { get; set; }
    /// <summary>The largest multi-kill of the match: 2 (double) to 5 (penta), 0 for none.</summary>
    public int BestMultikill { get; set; }
    public double GameSeconds { get; set; }
    /// <summary>Final place in a battle royale match (1 = win); 0 when the game has no placement.</summary>
    public int Placement { get; set; }
    public double Damage { get; set; }
    public int Headshots { get; set; }
    public double SurvivedSeconds { get; set; }
    public string Map { get; set; } = "";
}

/// <summary>Reads the League of Legends Live Client Data API ("allgamedata"). Missing or odd fields never throw; they give null or zeros.</summary>
public static class LolMatchParser
{
    public const string GameName = "League of Legends";

    /// <summary>True when any of the names being played is the League of Legends game (not its launcher).</summary>
    public static bool IsLeague(IEnumerable<string>? names) =>
        names != null && names.Any(n => (n ?? "").Contains("league of legends", StringComparison.OrdinalIgnoreCase));

    /// <summary>The summary of the active player, or null when the text is not a live match of a player (menus, replays, errors).</summary>
    public static GameMatchSummary? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("activePlayer", out var active) || active.ValueKind != JsonValueKind.Object) return null;

            var ids = Identities(active);
            if (ids.Count == 0) return null;

            JsonElement? me = null;
            if (root.TryGetProperty("allPlayers", out var players) && players.ValueKind == JsonValueKind.Array)
                foreach (var player in players.EnumerateArray())
                    if (player.ValueKind == JsonValueKind.Object && Identities(player).Overlaps(ids)) { me = player; break; }
            if (me is not { } mine) return null;

            var result = new GameMatchSummary { Game = GameName, Champion = Text(mine, "championName") };
            if (mine.TryGetProperty("scores", out var scores) && scores.ValueKind == JsonValueKind.Object)
            {
                result.Kills = Number(scores, "kills");
                result.Deaths = Number(scores, "deaths");
                result.Assists = Number(scores, "assists");
                result.CreepScore = Number(scores, "creepScore");
            }
            if (root.TryGetProperty("gameData", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                result.Mode = Text(data, "gameMode");
                if (data.TryGetProperty("gameTime", out var time) && time.ValueKind == JsonValueKind.Number) result.GameSeconds = time.GetDouble();
            }
            if (root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Object &&
                events.TryGetProperty("Events", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in list.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    string name = Text(e, "EventName");
                    if (name == "Multikill" && Identities(Text(e, "KillerName")).Overlaps(ids)) result.BestMultikill = Math.Max(result.BestMultikill, Math.Clamp(Number(e, "KillStreak"), 0, 5));
                    else if (name == "GameEnd") result.Result = Text(e, "Result") is { } r && r.StartsWith("Win", StringComparison.OrdinalIgnoreCase) ? "Win" : (Text(e, "Result").Length > 0 ? "Loss" : "");
                }
            }
            return result;
        }
        catch (JsonException) { return null; }
    }

    // The names a player is known by: "Name#TAG", the game name part, the old summoner name; compared ignoring case.
    private static HashSet<string> Identities(JsonElement player)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string s) { s = (s ?? "").Trim(); if (s.Length > 0) set.Add(s); }
        Add(Text(player, "riotId"));
        Add(Text(player, "summonerName"));
        string game = Text(player, "riotIdGameName"), tag = Text(player, "riotIdTagLine");
        Add(game);
        if (game.Length > 0 && tag.Length > 0) Add(game + "#" + tag);
        return set;
    }

    private static HashSet<string> Identities(string name)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(name))
        {
            set.Add(name.Trim());
            int hash = name.IndexOf('#');
            if (hash > 0) set.Add(name[..hash].Trim());
        }
        return set;
    }

    private static bool Overlaps(this HashSet<string> a, HashSet<string> b) => a.Overlaps((IEnumerable<string>)b);

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) ? n : 0;
}

/// <summary>Text for a match summary.</summary>
public static class GameMatchFormat
{
    private static readonly string[] MultikillNames = { "", "", "Double kill", "Triple kill", "Quadra kill", "Penta kill" };

    /// <summary>"#4 · 3 kills · 240 damage · 25m survived · Squad FPP · Erangel", for a match with a placement.</summary>
    private static string BattleRoyaleLine(GameMatchSummary m)
    {
        var parts = new List<string> { m.Placement == 1 ? "Winner winner" : "#" + m.Placement.ToString(CultureInfo.InvariantCulture) };
        parts.Add($"{m.Kills} kill{(m.Kills == 1 ? "" : "s")}");
        if (m.Damage > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{m.Damage:0} damage"));
        if (m.Headshots > 0) parts.Add($"{m.Headshots} headshot{(m.Headshots == 1 ? "" : "s")}");
        if (m.SurvivedSeconds >= 60) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{(int)(m.SurvivedSeconds / 60)}m survived"));
        if (m.Mode.Length > 0) parts.Add(m.Mode);
        if (m.Map.Length > 0) parts.Add(m.Map);
        return string.Join(" · ", parts);
    }

    /// <summary>"Win · Ahri · 8/3/12 · CS 187 · Triple kill", leaving out what the game did not report.</summary>
    public static string Line(GameMatchSummary? match)
    {
        if (match == null) return "";
        if (match.Placement > 0) return BattleRoyaleLine(match);
        var parts = new List<string>();
        if (match.Result.Length > 0) parts.Add(match.Result);
        if (match.Champion.Length > 0) parts.Add(match.Champion);
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"{match.Kills}/{match.Deaths}/{match.Assists}"));
        if (match.CreepScore > 0) parts.Add("CS " + match.CreepScore.ToString(CultureInfo.InvariantCulture));
        if (match.BestMultikill >= 2 && match.BestMultikill < MultikillNames.Length) parts.Add(MultikillNames[match.BestMultikill]);
        if (match.Mode.Length > 0 && !string.Equals(match.Mode, "CLASSIC", StringComparison.OrdinalIgnoreCase)) parts.Add(match.Mode);
        return string.Join(" · ", parts);
    }
}
