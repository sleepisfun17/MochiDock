using System.Net;
using System.Net.Sockets;
using MochiDock.Core;
using MochiDock.Services;

/// <summary>Runs the PUBG lookup against a local server that answers like the official API.</summary>
public class PubgStatsServiceTests
{
    const string Players = """{ "data": [ { "type": "player", "id": "account.abc", "relationships": { "matches": { "data": [ { "id": "m-new" }, { "id": "m-old" } ] } } } ] }""";
    static string Match(string id, string createdAt, int place) => $$"""
    { "data": { "id": "{{id}}", "attributes": { "createdAt": "{{createdAt}}", "duration": 1200, "gameMode": "duo-fpp", "mapName": "Desert_Main" } },
      "included": [ { "type": "participant", "attributes": { "stats": { "playerId": "account.abc", "kills": 5, "winPlace": {{place}}, "damageDealt": 600.0, "timeSurvived": 1100.0 } } } ] }
    """;

    sealed class Fake : IDisposable
    {
        readonly HttpListener listener = new();
        public string Url { get; }
        public List<(string Path, string? Auth, string? Accept)> Requests { get; } = new();
        public Dictionary<string, (int Status, string Body)> Routes { get; } = new();

        public Fake()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}";
            listener.Prefixes.Add(Url + "/");
            listener.Start();
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext c;
                    try { c = await listener.GetContextAsync(); } catch { return; }
                    lock (Requests) Requests.Add((c.Request.Url!.AbsolutePath, c.Request.Headers["Authorization"], c.Request.Headers["Accept"]));
                    var (status, body) = Routes.TryGetValue(c.Request.Url.AbsolutePath, out var r) ? r : (404, "");
                    c.Response.StatusCode = status;
                    var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                    await c.Response.OutputStream.WriteAsync(bytes);
                    c.Response.Close();
                }
            });
        }
        public void Dispose() { try { listener.Close(); } catch { } }
    }

    static readonly DateTime Start = new(2026, 11, 14, 11, 40, 0, DateTimeKind.Utc);
    static readonly DateTime End = new(2026, 11, 14, 12, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FindsTheMatchOfTheSessionAndSendsTheKeyAsABearerToken()
    {
        using var fake = new Fake();
        fake.Routes["/shards/steam/players"] = (200, Players);
        fake.Routes["/shards/steam/matches/m-new"] = (200, Match("m-new", "2026-11-14T12:00:00Z", 2));
        using var service = new PubgStatsService(fake.Url);

        var result = await service.FetchLatestAsync("steam", "Player", "secret-key", Start, End, default);

        Assert.Equal(PubgFetchStatus.Found, result.Status);
        Assert.Equal((5, 2, "Duo FPP", "Miramar"), (result.Match!.Kills, result.Match.Placement, result.Match.Mode, result.Match.Map));
        Assert.Equal("Bearer secret-key", fake.Requests[0].Auth);
        Assert.Equal("application/vnd.api+json", fake.Requests[0].Accept);
    }

    [Fact]
    public async Task AnOlderMatchIsNotThisSessionsMatchYet()
    {
        using var fake = new Fake();
        fake.Routes["/shards/steam/players"] = (200, Players);
        fake.Routes["/shards/steam/matches/m-new"] = (200, Match("m-new", "2026-11-14T09:00:00Z", 7));
        fake.Routes["/shards/steam/matches/m-old"] = (200, Match("m-old", "2026-11-13T09:00:00Z", 3));
        using var service = new PubgStatsService(fake.Url);

        var result = await service.FetchLatestAsync("steam", "Player", "k", Start, End, default);
        Assert.Equal(PubgFetchStatus.NotYet, result.Status);
        Assert.Null(result.Match);
    }

    [Theory]
    [InlineData(401, PubgFetchStatus.KeyRejected)]
    [InlineData(403, PubgFetchStatus.KeyRejected)]
    [InlineData(404, PubgFetchStatus.PlayerNotFound)]
    [InlineData(429, PubgFetchStatus.RateLimited)]
    [InlineData(500, PubgFetchStatus.Error)]
    public async Task FailuresAreNamed(int status, PubgFetchStatus expected)
    {
        using var fake = new Fake();
        fake.Routes["/shards/steam/players"] = (status, "");
        using var service = new PubgStatsService(fake.Url);
        Assert.Equal(expected, (await service.FetchLatestAsync("steam", "Player", "k", Start, End, default)).Status);
    }

    [Fact]
    public async Task OneUnreadableMatchDoesNotHideTheNextOne()
    {
        using var fake = new Fake();
        fake.Routes["/shards/steam/players"] = (200, Players);
        fake.Routes["/shards/steam/matches/m-new"] = (500, "");
        fake.Routes["/shards/steam/matches/m-old"] = (200, Match("m-old", "2026-11-14T12:00:00Z", 1));
        using var service = new PubgStatsService(fake.Url);

        var result = await service.FetchLatestAsync("steam", "Player", "k", Start, End, default);
        Assert.Equal(PubgFetchStatus.Found, result.Status);
        Assert.Equal("Win", result.Match!.Result);
    }

    [Fact]
    public async Task MissingNameOrKeyMakesNoRequest()
    {
        using var fake = new Fake();
        using var service = new PubgStatsService(fake.Url);
        Assert.Equal(PubgFetchStatus.Error, (await service.FetchLatestAsync("steam", "", "k", Start, End, default)).Status);
        Assert.Equal(PubgFetchStatus.Error, (await service.FetchLatestAsync("steam", "Player", " ", Start, End, default)).Status);
        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData("STEAM", "steam")]
    [InlineData(" xbox ", "xbox")]
    [InlineData("psn", "psn")]
    [InlineData("kakao", "kakao")]
    [InlineData("pc-nonsense", "steam")]
    [InlineData(null, "steam")]
    public void PlatformsAreRestrictedToTheKnownShards(string? platform, string expected) =>
        Assert.Equal(expected, PubgStatsService.NormalizePlatform(platform));
}
