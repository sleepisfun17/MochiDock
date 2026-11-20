using System.Net;
using System.Net.Sockets;
using MochiDock.Models;
using MochiDock.Services;

/// <summary>Runs <see cref="GowaService.CheckConnectionAsync"/> against a small local HTTP server standing in for GOWA (Remote mode).</summary>
public class GowaServiceConnectionTests
{
    sealed class FakeServer : IDisposable
    {
        readonly HttpListener _listener = new();
        readonly Dictionary<string, (int Status, string Body)> _routes;
        public List<string> Paths { get; } = new();
        public string Url { get; }

        public FakeServer(Dictionary<string, (int, string)> routes)
        {
            _routes = routes;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            _ = Task.Run(Loop);
        }

        async Task Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); } catch { return; }
                string path = context.Request.Url!.AbsolutePath;
                lock (Paths) Paths.Add(path);
                var (status, body) = _routes.TryGetValue(path, out var route) ? route : (404, "{}");
                var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        public void Dispose() { try { _listener.Close(); } catch { } }
    }

    static GowaService Remote(string url)
    {
        var service = new GowaService();
        // A token is given so the service does not generate one and save the user's real settings file.
        service.ConfigureFromSettings(new AppSettings { EnableWhatsApp = true, GowaMode = "Remote", RemoteGowaUrl = url, RemoteWebhookToken = "test-token" });
        return service;
    }

    [Fact]
    public async Task ListsEveryLinkedAccountFromDevices()
    {
        using var server = new FakeServer(new()
        {
            ["/devices"] = (200, """{"code":"SUCCESS","results":[{"id":"user-a","display_name":"Player","jid":"6281@s.whatsapp.net","state":"logged_in"},{"id":"user-b","display_name":"Player JP","jid":"8190@s.whatsapp.net","state":"logged_in"}]}""")
        });
        var service = Remote(server.Url);

        Assert.True(await service.CheckConnectionAsync());
        Assert.Equal(2, service.Accounts.Count);
        Assert.Equal("user-a", service.ConnectedDeviceId);
        Assert.Equal("", service.ConnectionProblem);
        Assert.DoesNotContain("/app/devices", server.Paths);   // /devices answered; the fallback is not needed
    }

    [Fact]
    public async Task AnEmptyDeviceListMeansNoLinkedAccount()
    {
        using var server = new FakeServer(new() { ["/devices"] = (200, """{"results":[]}""") });
        var service = Remote(server.Url);

        Assert.False(await service.CheckConnectionAsync());
        Assert.Equal("no WhatsApp device linked", service.ConnectionProblem);
        Assert.Empty(service.Accounts);
    }

    [Fact]
    public async Task OlderServersFallBackToAppDevices()
    {
        using var server = new FakeServer(new()
        {
            ["/devices"] = (404, "{}"),
            ["/app/devices"] = (200, """{"results":[{"device":"old-1","name":"Phone","state":"logged_in"}]}""")
        });
        var service = Remote(server.Url);

        Assert.True(await service.CheckConnectionAsync());
        Assert.Single(service.Accounts);
    }

    [Fact]
    public async Task RejectedLoginIsReportedAsSuch()
    {
        using var server = new FakeServer(new() { ["/devices"] = (401, "{}"), ["/app/devices"] = (401, "{}") });
        var service = Remote(server.Url);

        Assert.False(await service.CheckConnectionAsync());
        Assert.Equal("login rejected", service.ConnectionProblem);
    }

    [Fact]
    public async Task AServerThatIsNotRunningIsUnreachable()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var service = Remote($"http://127.0.0.1:{port}");

        Assert.False(await service.CheckConnectionAsync());
        Assert.Equal("unreachable", service.ConnectionProblem);
    }

    [Fact]
    public void MutesAreScopedToTheAccountOnlyWithSeveralAccounts()
    {
        var service = Remote("http://127.0.0.1:1");
        service.AddMutedChat("dev-1|123@s.whatsapp.net");
        service.AddMutedChat("999@g.us");

        Assert.True(service.IsChatMuted("123@s.whatsapp.net", false, "dev-1"));
        Assert.False(service.IsChatMuted("123@s.whatsapp.net", false, "dev-2"));
        Assert.True(service.IsChatMuted("999@g.us", true, "dev-2"));
    }

    [Fact]
    public void OnlyListedGroupsModeFollowsTheSettingsAndTheRunningList()
    {
        var service = new GowaService();
        service.ConfigureFromSettings(new AppSettings { EnableWhatsApp = true, GowaMode = "Remote", RemoteGowaUrl = "http://127.0.0.1:1", RemoteWebhookToken = "t",
            WhatsAppOnlyListedGroups = true, WhatsAppAllowedGroups = new() { "111@g.us" } });

        Assert.False(service.IsChatMuted("111@g.us", true, "dev-1"));
        Assert.True(service.IsChatMuted("222@g.us", true, "dev-1"));
        Assert.False(service.IsChatMuted("628123@s.whatsapp.net", false, "dev-1"));   // direct chats are not affected

        service.AddAllowedGroup("222@g.us");                                            // "Always notify" works without a restart
        Assert.False(service.IsChatMuted("222@g.us", true, "dev-1"));

        service.ConfigureFromSettings(new AppSettings { EnableWhatsApp = true, GowaMode = "Remote", RemoteGowaUrl = "http://127.0.0.1:1", RemoteWebhookToken = "t" });
        Assert.False(service.IsChatMuted("333@g.us", true, "dev-1"));                   // switched off again
    }
}
