using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace MochiDock.Core;

/// <summary>The player and the ids of their latest matches, from the PUBG "players" answer.</summary>
public sealed record PubgPlayer(string AccountId, IReadOnlyList<string> MatchIds);

/// <summary>A parsed PUBG match: the player's numbers and when the match ended (UTC).</summary>
public sealed record PubgMatch(GameMatchSummary Summary, DateTime EndedUtc);

/// <summary>Reads the official PUBG API answers (JSON:API). Missing or odd fields never throw.</summary>
public static class PubgMatchParser
{
    public const string GameName = "PUBG";

    /// <summary>True when any of the names is PUBG: BATTLEGROUNDS (window title or the TslGame process).</summary>
    public static bool IsPubg(IEnumerable<string>? names) =>
        names != null && names.Any(n =>
        {
            string name = n ?? "";
            return name.Contains("pubg", StringComparison.OrdinalIgnoreCase) || name.Contains("tslgame", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("battlegrounds", StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>The account id and match ids (newest first) of the first player in a "players" answer, or null.</summary>
    public static PubgPlayer? ParsePlayer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) return null;
            var player = data[0];
            string id = Text(player, "id");
            if (id.Length == 0) return null;
            var ids = new List<string>();
            if (player.TryGetProperty("relationships", out var rel) && rel.ValueKind == JsonValueKind.Object &&
                rel.TryGetProperty("matches", out var matches) && matches.ValueKind == JsonValueKind.Object &&
                matches.TryGetProperty("data", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var m in list.EnumerateArray()) if (m.ValueKind == JsonValueKind.Object && Text(m, "id") is { Length: > 0 } matchId) ids.Add(matchId);
            return new PubgPlayer(id, ids);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The player's numbers in a match, or null when the match has no participant with that account id.</summary>
    public static PubgMatch? ParseMatch(string? json, string accountId)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(accountId)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return null;
            if (!data.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object) return null;

            JsonElement? stats = null;
            if (root.TryGetProperty("included", out var included) && included.ValueKind == JsonValueKind.Array)
                foreach (var item in included.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || Text(item, "type") != "participant") continue;
                    if (!item.TryGetProperty("attributes", out var a) || a.ValueKind != JsonValueKind.Object) continue;
                    if (!a.TryGetProperty("stats", out var s) || s.ValueKind != JsonValueKind.Object) continue;
                    if (string.Equals(Text(s, "playerId"), accountId, StringComparison.OrdinalIgnoreCase)) { stats = s; break; }
                }
            if (stats is not { } mine) return null;

            double duration = Double(attributes, "duration");
            DateTime created = DateTime.TryParse(Text(attributes, "createdAt"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var c) ? c : DateTime.MinValue;
            var summary = new GameMatchSummary
            {
                Game = GameName,
                Kills = (int)Double(mine, "kills"),
                Placement = (int)Double(mine, "winPlace"),
                Damage = Double(mine, "damageDealt"),
                Headshots = (int)Double(mine, "headshotKills"),
                SurvivedSeconds = Double(mine, "timeSurvived"),
                GameSeconds = duration,
                Mode = ModeName(Text(attributes, "gameMode")),
                Map = MapName(Text(attributes, "mapName")),
            };
            if (summary.Placement == 1) summary.Result = "Win";
            return new PubgMatch(summary, created == DateTime.MinValue ? DateTime.MinValue : created.AddSeconds(duration));
        }
        catch (JsonException) { return null; }
    }

    /// <summary>"squad-fpp" becomes "Squad FPP", "duo" "Duo".</summary>
    public static string ModeName(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "";
        return string.Join(" ", mode.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Equals("fpp", StringComparison.OrdinalIgnoreCase) ? "FPP" : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }

    /// <summary>The map names the API uses, in the names players know.</summary>
    public static string MapName(string? map) => (map ?? "").Trim() switch
    {
        "Baltic_Main" or "Erangel_Main" => "Erangel",
        "Desert_Main" => "Miramar",
        "DihorOtok_Main" => "Vikendi",
        "Savage_Main" => "Sanhok",
        "Summerland_Main" => "Karakin",
        "Chimera_Main" => "Paramo",
        "Tiger_Main" => "Taego",
        "Kiki_Main" => "Deston",
        "Neon_Main" => "Rondo",
        "Heaven_Main" => "Haven",
        "Range_Main" => "Camp Jackal",
        var other => other.Replace("_Main", "").Replace('_', ' '),
    };

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static double Double(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
}

/// <summary>When a finished PUBG session's match counts as the one that was just played.</summary>
public static class PubgMatchWindow
{
    /// <summary>
    /// A match belongs to a session when it ended after the session started and not later than ten minutes after the session ended
    /// (the match record is created when the match ends; the game process keeps running through lobbies and the after-match screen).
    /// </summary>
    public static bool Contains(DateTime matchEndedUtc, DateTime sessionStartUtc, DateTime sessionEndUtc) =>
        matchEndedUtc != DateTime.MinValue && matchEndedUtc >= sessionStartUtc && matchEndedUtc <= sessionEndUtc.AddMinutes(10);
}
