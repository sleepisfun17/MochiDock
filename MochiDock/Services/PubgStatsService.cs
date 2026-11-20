using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using MochiDock.Core;

namespace MochiDock.Services;

public enum PubgFetchStatus { Found, NotYet, KeyRejected, PlayerNotFound, RateLimited, Error }

public sealed record PubgFetchResult(PubgFetchStatus Status, GameMatchSummary? Match = null);

/// <summary>
/// Looks up the match that was just played in the official PUBG API (https://api.pubg.com, a free developer key). Only the
/// "players" lookup counts against the key's limit of 10 requests per minute; this makes one such request per attempt.
/// The key is sent only to the configured API host and is never logged.
/// </summary>
public sealed class PubgStatsService : IDisposable
{
    public const string DefaultBaseUrl = "https://api.pubg.com";
    public static readonly string[] Platforms = { "steam", "kakao", "xbox", "psn" };
    private const int MatchesChecked = 3;
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public PubgStatsService(string? baseUrl = null)
    {
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public static string NormalizePlatform(string? platform) =>
        Platforms.FirstOrDefault(p => string.Equals(p, (platform ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) ?? "steam";

    /// <summary>
    /// The player's latest match that ended inside the session window, or why there is none yet. "NotYet" is normal for the first
    /// minutes after a match: the API publishes it late.
    /// </summary>
    public async Task<PubgFetchResult> FetchLatestAsync(string platform, string playerName, string apiKey, DateTime sessionStartUtc, DateTime sessionEndUtc, CancellationToken token)
    {
        platform = NormalizePlatform(platform);
        if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(apiKey)) return new(PubgFetchStatus.Error);
        try
        {
            var (playersStatus, playersJson) = await GetAsync($"{_baseUrl}/shards/{platform}/players?filter[playerNames]={Uri.EscapeDataString(playerName.Trim())}", apiKey, token).ConfigureAwait(false);
            var early = Failure(playersStatus);
            if (early != null) return early;
            var player = PubgMatchParser.ParsePlayer(playersJson);
            if (player == null) return new(PubgFetchStatus.PlayerNotFound);

            foreach (var id in player.MatchIds.Take(MatchesChecked))
            {
                var (matchStatus, matchJson) = await GetAsync($"{_baseUrl}/shards/{platform}/matches/{Uri.EscapeDataString(id)}", apiKey, token).ConfigureAwait(false);
                if (matchStatus != HttpStatusCode.OK) continue;      // one unreadable match must not hide the next one
                var match = PubgMatchParser.ParseMatch(matchJson, player.AccountId);
                if (match != null && PubgMatchWindow.Contains(match.EndedUtc, sessionStartUtc, sessionEndUtc)) return new(PubgFetchStatus.Found, match.Summary);
            }
            return new(PubgFetchStatus.NotYet);
        }
        catch (HttpRequestException) { return new(PubgFetchStatus.Error); }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { return new(PubgFetchStatus.Error); }   // the request timed out
    }

    private static PubgFetchResult? Failure(HttpStatusCode status) => status switch
    {
        HttpStatusCode.OK => null,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(PubgFetchStatus.KeyRejected),
        HttpStatusCode.NotFound => new(PubgFetchStatus.PlayerNotFound),
        HttpStatusCode.TooManyRequests => new(PubgFetchStatus.RateLimited),
        _ => new(PubgFetchStatus.Error),
    };

    private async Task<(HttpStatusCode Status, string Body)> GetAsync(string url, string apiKey, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        string body = response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStringAsync(token).ConfigureAwait(false) : "";
        return (response.StatusCode, body);
    }

    public void Dispose() => _http.Dispose();
}
