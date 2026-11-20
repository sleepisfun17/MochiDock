using System.Net;
using System.Net.Http;
using System.Text;
using MochiDock.Core;
using MochiDock.Services;

public class CalendarFeedServiceTests : IDisposable
{
    sealed class PlainProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plain) => plain.Select(b => (byte)(b ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] sealedBytes) => sealedBytes.Select(b => (byte)(b ^ 0x5A)).ToArray();
    }

    sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
        public Task? Gate;   // when set, responses wait for it (asynchronously)
        public readonly List<HttpRequestMessage> Requests = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            await Task.Yield();
            if (Gate is not null) await Gate;
            return Respond(request);
        }
    }

    const string Url = "https://calendar.google.com/calendar/ical/me%40example.com/private-secret123/basic.ics";
    static readonly DateTime Today = new(2026, 11, 11, 10, 0, 0);

    readonly string folder = Path.Combine(Path.GetTempPath(), "mochidock-cal-" + Guid.NewGuid().ToString("N"));
    readonly FakeHandler handler = new();

    public void Dispose() { try { Directory.Delete(folder, true); } catch (IOException) { } }

    DateTime clock = Today;
    CalendarFeedService Create() => new(new PlainProtector(), new HttpClient(handler), folder, () => clock);

    static HttpResponseMessage Ics(string body, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/calendar") };
        if (etag is not null) response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return response;
    }

    static string Feed(string summary = "Design review") =>
        $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:1\r\nSUMMARY:{summary}\r\nDTSTART;TZID=Asia/Tokyo:20261111T140000\r\nDTEND;TZID=Asia/Tokyo:20261111T150000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "Timed out waiting for the calendar service");
    }

    [Fact]
    public void RejectsLinksThatAreNotHttpsFeeds()
    {
        using var service = Create();
        Assert.NotEqual("", service.Connect("http://example.com/a.ics"));
        Assert.NotEqual("", service.Connect("hello"));
        Assert.False(service.IsConnected);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ConnectFetchesParsesAndCaches()
    {
        handler.Respond = _ => Ics(Feed(), "\"v1\"");
        using var service = Create();
        Assert.Equal("", service.Connect("webcal://calendar.google.com/calendar/ical/me%40example.com/private-secret123/basic.ics"));
        await WaitFor(() => service.State == CalendarFeedState.Updated && !service.IsRefreshing);

        Assert.Single(service.Events);
        Assert.Equal("Design review", service.Events[0].Title);
        Assert.StartsWith("https://", handler.Requests[0].RequestUri!.ToString());   // webcal:// is fetched over https
        Assert.True(File.Exists(Path.Combine(folder, "Calendar", "events.json")));
    }

    [Fact]
    public async Task LinkIsStoredSealedAndNeverInPlainText()
    {
        handler.Respond = _ => Ics(Feed());
        using (var service = Create()) { service.Connect(Url); await WaitFor(() => service.State == CalendarFeedState.Updated && !service.IsRefreshing); }
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            Assert.DoesNotContain("private-secret123", text);
        }
    }

    [Fact]
    public async Task ReconnectingLoadsTheSavedLinkAndCachedAgendaWithoutTheNetwork()
    {
        handler.Respond = _ => Ics(Feed());
        using (var first = Create()) { first.Connect(Url); await WaitFor(() => first.State == CalendarFeedState.Updated && !first.IsRefreshing); }

        handler.Requests.Clear();
        using var second = Create();
        Assert.True(second.IsConnected);
        Assert.Single(second.Events);
        Assert.Equal(CalendarFeedState.Updated, second.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task BackgroundRefreshNeedsAConnectedFeedAndStopsOnDisconnect()
    {
        handler.Respond = _ => Ics(Feed());
        using var service = Create();
        service.SetBackground(true);
        Assert.False(service.IsBackgroundRefreshing);   // nothing connected yet

        service.Connect(Url);
        await WaitFor(() => service.State == CalendarFeedState.Updated && !service.IsRefreshing);
        Assert.True(service.IsBackgroundRefreshing);

        service.SetBackground(false);
        Assert.False(service.IsBackgroundRefreshing);
        service.SetBackground(true);
        Assert.True(service.IsBackgroundRefreshing);
        service.Disconnect();
        Assert.False(service.IsBackgroundRefreshing);
    }

    [Fact]
    public async Task ServerNotModifiedKeepsEventsAndSendsTheEtag()
    {
        handler.Respond = _ => Ics(Feed(), "\"v1\"");
        using var service = Create();
        service.Connect(Url);
        await WaitFor(() => service.State == CalendarFeedState.Updated && !service.IsRefreshing);

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotModified);
        // A forced refresh skips the conditional request, so the 304 path is exercised by an unforced one after the minimum gap has passed.
        clock = Today.AddMinutes(10);
        await service.RefreshAsync(force: false);
        Assert.Contains(handler.Requests, r => r.Headers.TryGetValues("If-None-Match", out var v) && v.Contains("\"v1\""));
        Assert.Single(service.Events);
        Assert.Equal(CalendarFeedState.Updated, service.State);
    }

    [Fact]
    public async Task ShowingTheCardRefreshesWhenStaleButNotWithinAMinute()
    {
        handler.Respond = _ => Ics(Feed());
        using var service = Create();
        service.Connect(Url);
        await WaitFor(() => service.State == CalendarFeedState.Updated && !service.IsRefreshing);
        int afterConnect = handler.Requests.Count;

        service.SetVisible(false);
        service.SetVisible(true);                       // within the minimum gap: no new request
        await Task.Delay(100);
        Assert.Equal(afterConnect, handler.Requests.Count);

        service.SetVisible(false);
        clock = Today.AddMinutes(5);                    // stale: showing the card refreshes immediately
        service.SetVisible(true);
        await WaitFor(() => handler.Requests.Count == afterConnect + 1 && !service.IsRefreshing);
        service.SetVisible(false);
    }

    [Fact]
    public async Task NetworkFailureKeepsTheLastAgendaAndMarksOffline()
    {
        handler.Respond = _ => Ics(Feed());
        using var service = Create();
        service.Connect(Url);
        await WaitFor(() => service.State == CalendarFeedState.Updated && !service.IsRefreshing);

        handler.Respond = _ => throw new HttpRequestException("offline");
        await service.RefreshAsync(force: true);
        Assert.Equal(CalendarFeedState.Offline, service.State);
        Assert.Single(service.Events);
        Assert.StartsWith("Offline", service.StatusText);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedLinkIsReportedAsInvalid(HttpStatusCode code)
    {
        handler.Respond = _ => new HttpResponseMessage(code);
        using var service = Create();
        service.Connect(Url);
        await WaitFor(() => service.State == CalendarFeedState.Invalid && !service.IsRefreshing);
        Assert.Contains(((int)code).ToString(), service.StatusText);
    }

    [Fact]
    public async Task NonCalendarBodyIsInvalid()
    {
        handler.Respond = _ => Ics("<html>sign in</html>");
        using var service = Create();
        service.Connect(Url);
        await WaitFor(() => service.State == CalendarFeedState.Invalid && !service.IsRefreshing);
        Assert.Empty(service.Events);
    }

    [Fact]
    public async Task DisconnectForgetsEverythingAndDropsLateResults()
    {
        var release = new TaskCompletionSource();
        var slow = new FakeHandler();
        slow.Gate = release.Task;
        slow.Respond = _ => Ics(Feed());
        using var service = new CalendarFeedService(new PlainProtector(), new HttpClient(slow), folder, () => Today);
        service.Connect(Url);
        await WaitFor(() => slow.Requests.Count == 1);

        service.Disconnect();
        release.SetResult();
        await Task.Delay(150);

        Assert.False(service.IsConnected);
        Assert.Equal(CalendarFeedState.NotConnected, service.State);
        Assert.Empty(service.Events);
        Assert.False(File.Exists(Path.Combine(folder, "gcal.dat")));
        Assert.False(File.Exists(Path.Combine(folder, "Calendar", "events.json")));
    }

    [Fact]
    public async Task OversizedFeedIsRefused()
    {
        handler.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("BEGIN:VCALENDAR\r\n" + new string('x', CalendarFeedService.MaxFeedBytes + 10)) };
            return response;
        };
        using var service = Create();
        service.Connect(Url);
        await WaitFor(() => service.State == CalendarFeedState.Invalid && !service.IsRefreshing);
        Assert.Contains("too large", service.StatusText);
    }
}
