using System.Net;
using System.Net.Sockets;
using MochiDock.Services;

/// <summary>Runs the service against a small local HTTP server that answers like the game's Live Client Data API.</summary>
public class LolLiveClientServiceTests
{
    const string Answer = """
    { "activePlayer": { "riotId": "A#1" },
      "allPlayers": [ { "championName": "Ahri", "riotId": "A#1", "scores": { "kills": 2, "deaths": 1, "assists": 4, "creepScore": 90 } } ],
      "events": { "Events": [ { "EventName": "GameEnd", "Result": "Win" } ] }, "gameData": { "gameMode": "CLASSIC", "gameTime": 900 } }
    """;

    static (HttpListener Listener, string Url) Server(int status, string body)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var listener = new HttpListener();
        string url = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add(url + "/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); } catch { return; }
                context.Response.StatusCode = status;
                var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });
        return (listener, url);
    }

    [Fact]
    public async Task ReadsTheMatchFromTheLocalApi()
    {
        var (listener, url) = Server(200, Answer);
        try
        {
            using var service = new LolLiveClientService(url);
            Assert.True(await service.PollOnceAsync());
            Assert.Equal("Ahri", service.Latest!.Champion);
            Assert.Equal(2, service.Latest.Kills);
            Assert.Equal("Win", service.Latest.Result);
        }
        finally { listener.Close(); }
    }

    [Fact]
    public async Task ANotFoundAnswerIsJustNoMatch()
    {
        var (listener, url) = Server(404, """{"errorCode":"RESOURCE_NOT_FOUND"}""");
        try
        {
            using var service = new LolLiveClientService(url);
            Assert.False(await service.PollOnceAsync());
            Assert.Null(service.Latest);
        }
        finally { listener.Close(); }
    }

    [Fact]
    public async Task ACloseGameMeansNothingToReadAndNoCrash()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var service = new LolLiveClientService($"http://127.0.0.1:{port}");
        Assert.False(await service.PollOnceAsync());
    }

    [Fact]
    public async Task TheBackgroundLoopKeepsTheLastAnswerAfterStop()
    {
        var (listener, url) = Server(200, Answer);
        try
        {
            using var service = new LolLiveClientService(url, TimeSpan.FromMilliseconds(50));
            service.Start();
            Assert.True(service.IsRunning);
            for (int i = 0; i < 100 && service.Latest == null; i++) await Task.Delay(50);
            Assert.NotNull(service.Latest);
            service.Stop();
            Assert.False(service.IsRunning);
            Assert.NotNull(service.Latest);                // the API vanishes with the game; the last answer is what we keep
            service.Start();                               // a new match forgets the old one
            Assert.True(service.IsRunning);
        }
        finally { listener.Close(); }
    }
}
