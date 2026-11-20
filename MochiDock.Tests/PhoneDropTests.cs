using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.Tests;

public sealed class PhoneDropTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AuraDrop-" + Guid.NewGuid().ToString("N"));
    private PhoneDropServer _server = null!;
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private DateTimeOffset _clock = DateTimeOffset.UtcNow;
    private int _port;
    public async Task InitializeAsync()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        _port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        _server = new PhoneDropServer(_root, () => _clock, TimeSpan.FromSeconds(60));
        await _server.StartAsync(IPAddress.Loopback, _port);
        _client.BaseAddress = new Uri($"http://127.0.0.1:{_port}/");
        SetToken();
    }
    private void SetToken() => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new Uri(_server.Url).Fragment.TrimStart('#'));
    private async Task<string> Propose(params DropFile[] files)
    {
        using var content = new StringContent(JsonSerializer.Serialize(files), System.Text.Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("requests", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }
    [Fact] public async Task UnauthorizedRequestCannotProposeOrUpload()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        using var proposal = await _client.PostAsJsonAsync("requests", new[] { new DropFile("a.txt",1) });
        using var upload = await _client.PostAsync("uploads/no/0",new ByteArrayContent(new byte[1]));
        Assert.Equal(HttpStatusCode.Forbidden,proposal.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,upload.StatusCode);
        Assert.Empty(Directory.GetFiles(_root));
    }
    [Fact] public async Task PendingAndRejectedRequestsCannotWrite()
    {
        string id = await Propose(new DropFile("a.txt",1));
        using var pending = await _client.PostAsync($"uploads/{id}/0",new ByteArrayContent(new byte[1]));
        Assert.Equal(HttpStatusCode.Conflict,pending.StatusCode);
        Assert.True(_server.Answer(id,false));
        using var rejected = await _client.PostAsync($"uploads/{id}/0",new ByteArrayContent(new byte[1]));
        Assert.Equal(HttpStatusCode.Conflict,rejected.StatusCode);
        Assert.Empty(Directory.GetFiles(_root));
    }
    [Fact] public async Task ApprovalExpiresAndCannotBeAcceptedLate()
    {
        string id = await Propose(new DropFile("a.txt",1)); _clock += TimeSpan.FromSeconds(61);
        Assert.False(_server.Answer(id,true));
        using var status = await _client.GetAsync("requests/"+id);
        var json = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("expired",json.GetProperty("state").GetString());
    }
    [Fact] public async Task ApprovedUploadsNeverOverwriteOrEscape()
    {
        File.WriteAllText(Path.Combine(_root,"a.txt"),"original");
        string id = await Propose(new DropFile("../../a.txt",3)); Assert.True(_server.Answer(id,true));
        using var response = await _client.PostAsync($"uploads/{id}/0",new ByteArrayContent(new byte[]{1,2,3}));
        response.EnsureSuccessStatusCode();
        Assert.Equal("original",File.ReadAllText(Path.Combine(_root,"a.txt")));
        Assert.Equal(new byte[]{1,2,3},File.ReadAllBytes(Path.Combine(_root,"a (1).txt")));
        Assert.Empty(Directory.GetFiles(_root,".partial-*"));
        using var replay = await _client.PostAsync($"uploads/{id}/0",new ByteArrayContent(new byte[]{1,2,3}));
        Assert.Equal(HttpStatusCode.Conflict,replay.StatusCode);
    }
    [Fact] public async Task OversizedAndTamperedRequestsWriteNothing()
    {
        using var largeContent = new StringContent(JsonSerializer.Serialize(new[]{new DropFile("big",PhoneDropPolicy.MaxFileBytes+1)}), System.Text.Encoding.UTF8, "application/json");
        using var oversized = await _client.PostAsync("requests",largeContent);
        Assert.False(oversized.IsSuccessStatusCode);
        string id = await Propose(new DropFile("a.txt",2)); _server.Answer(id,true);
        using var tampered = await _client.PostAsync($"uploads/{id}/0",new ByteArrayContent(new byte[3]));
        Assert.Equal(HttpStatusCode.BadRequest,tampered.StatusCode);
        Assert.Empty(Directory.GetFiles(_root));
    }
    [Fact] public async Task RestartInvalidatesOldSecretAndApprovals()
    {
        string id = await Propose(new DropFile("a",1)); _server.Answer(id,true);
        await _server.StopAsync(); await _server.StartAsync(IPAddress.Loopback,_port);
        using var oldToken = await _client.GetAsync("requests/"+id);
        Assert.Equal(HttpStatusCode.Forbidden,oldToken.StatusCode);
        SetToken(); using var oldApproval = await _client.PostAsync($"uploads/{id}/0",new ByteArrayContent(new byte[1]));
        Assert.Equal(HttpStatusCode.Conflict,oldApproval.StatusCode);
    }
    [Fact] public async Task InactiveSessionIsRejected()
    {
        _clock += TimeSpan.FromMinutes(16);
        using var response = await _client.PostAsJsonAsync("requests",new[]{new DropFile("a",1)});
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }
    private sealed class PausedContent : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override bool TryComputeLength(out long length) { length=5;return true; }
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context) => SerializeToStreamAsync(stream,context,CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream,TransportContext? context,CancellationToken token)
        { await stream.WriteAsync(new byte[]{1},token);await stream.FlushAsync(token);Started.TrySetResult();await Task.Delay(Timeout.Infinite,token); }
    }
    [Fact] public async Task StopCancelsPartialUploadAndCleansStaging()
    {
        string id = await Propose(new DropFile("partial.bin",5)); _server.Answer(id,true);
        using var content = new PausedContent(); using var cancellation = new CancellationTokenSource();
        var receiving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Changed += () => { if (_server.Status.StartsWith("Receiving")) receiving.TrySetResult(); };
        Task<HttpResponseMessage> sending = _client.PostAsync($"uploads/{id}/0",content,cancellation.Token);
        try
        {
            await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Directory.GetFiles(_root,".partial-*").Length == 0) await Task.Delay(10,deadline.Token);
            await _server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(Directory.GetFiles(_root));
        }
        finally { cancellation.Cancel(); try { using var ignored = await sending; } catch { } }
    }
    [Theory] [InlineData("../../CON.txt","_CON.txt")] [InlineData("C:\\temp\\a?.txt","a_.txt")] [InlineData("..","upload")] [InlineData("x.exe.","x.exe")]
    public void WindowsNamesAreSafe(string original,string expected) => Assert.Equal(expected,PhoneDropPolicy.SafeName(original));
    [Fact] public void BatchLimitsAreEnforced()
    {
        Assert.Throws<ArgumentException>(()=>PhoneDropPolicy.Validate(Enumerable.Repeat(new DropFile("x",1),11).ToArray()));
        Assert.Throws<ArgumentException>(()=>PhoneDropPolicy.Validate(Enumerable.Repeat(new DropFile("x",PhoneDropPolicy.MaxFileBytes),3).ToArray()));
        Assert.Throws<ArgumentException>(()=>PhoneDropPolicy.Validate(new[]{new DropFile("x",-1)}));
    }
    [Fact] public async Task ChunkedProposalIsRejectedBeforeApproval()
    {
        using var content = JsonContent.Create(new[]{new DropFile("a",1)});
        using var response = await _client.PostAsync("requests",content);
        Assert.Equal(HttpStatusCode.LengthRequired,response.StatusCode);
        Assert.False(_server.HasPendingApproval);
        Assert.Empty(Directory.GetFiles(_root));
    }
    private async Task WaitForPartials(int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Directory.GetFiles(_root,".partial-*").Length != count) await Task.Delay(10,deadline.Token);
    }
    [Fact] public async Task ThirdConcurrentUploadIsRejected()
    {
        string id = await Propose(new DropFile("one",5),new DropFile("two",5),new DropFile("three",5));
        _server.Answer(id,true);
        using var first = new PausedContent(); using var second = new PausedContent(); using var cancellation = new CancellationTokenSource();
        var sendingFirst = _client.PostAsync($"uploads/{id}/0",first,cancellation.Token);
        var sendingSecond = _client.PostAsync($"uploads/{id}/1",second,cancellation.Token);
        try
        {
            await WaitForPartials(2);
            using var third = await _client.PostAsync($"uploads/{id}/2",new ByteArrayContent(new byte[5]));
            Assert.Equal((HttpStatusCode)429,third.StatusCode);
            await _server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(Directory.GetFiles(_root));
        }
        finally
        {
            cancellation.Cancel();
            try { using var ignored = await sendingFirst; } catch { }
            try { using var ignored = await sendingSecond; } catch { }
        }
    }
    [Fact] public async Task IdleUploadTimeoutRemovesPartialFile()
    {
        await _server.StopAsync();
        _server = new PhoneDropServer(_root,()=>_clock,TimeSpan.FromMilliseconds(250));
        await _server.StartAsync(IPAddress.Loopback,_port); SetToken();
        string id = await Propose(new DropFile("idle",5)); _server.Answer(id,true);
        using var content = new PausedContent(); using var cancellation = new CancellationTokenSource();
        var sending = _client.PostAsync($"uploads/{id}/0",content,cancellation.Token);
        try
        {
            await WaitForPartials(1);
            await WaitForPartials(0);
            Assert.Empty(Directory.GetFiles(_root));
        }
        finally { cancellation.Cancel();try { using var ignored = await sending; } catch { } }
    }
    public async Task DisposeAsync() { await _server.StopAsync();_client.Dispose();if(Directory.Exists(_root))Directory.Delete(_root,true); }
}
