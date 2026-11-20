using System.Net;
using MochiDock.Services;

public class LyricRomajiPreferenceTests
{
    sealed class Handler(Func<HttpRequestMessage, string> body) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body(request)) });
        }
    }

    const string Romaji = "[00:01]kimi no koe ga kikoeru yo\\n[00:05]mou ichido dake aitai\\n[00:09]sekai ga owaru made zutto issho ni ite\\n[00:13]kokoro no naka de";
    const string Native = "[00:01]君の声が聞こえるよ\\n[00:05]もう一度だけ会いたい\\n[00:09]世界が終わるまでずっと一緒にいて\\n[00:13]心の中で";

    static string Record(string synced) =>
        $$"""{"trackName":"Kimi no Koe","artistName":"Artist","duration":200,"syncedLyrics":"{{synced}}"}""";

    static async Task<(SongLyrics? Song, Handler Handler)> Fetch(Func<HttpRequestMessage, string> body)
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var handler = new Handler(body);
        try { return (await new LrclibService(new HttpClient(handler), dir).FetchLyricsAsync("Kimi no Koe", "Artist", "", 200), handler); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task NativeScriptBeatsARomajiUploadInTheSameSearch()
    {
        var (song, _) = await Fetch(r => r.RequestUri!.AbsolutePath.EndsWith("get") ? "" : $"[{Record(Romaji)},{Record(Native)}]");
        Assert.True(song!.IsSynced);
        Assert.StartsWith("君の声", song.Lines[0].Text);
    }

    [Fact]
    public async Task ARomajiExactMatchKeepsSearchingForTheOriginal()
    {
        var (song, handler) = await Fetch(r => r.RequestUri!.AbsolutePath.EndsWith("get") ? Record(Romaji) : $"[{Record(Native)}]");
        Assert.StartsWith("君の声", song!.Lines[0].Text);
        Assert.Contains(handler.Requests, q => q.Contains("/search", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RomajiIsStillShownWhenItIsTheOnlyVersion()
    {
        var (song, _) = await Fetch(r => r.RequestUri!.AbsolutePath.EndsWith("get") ? Record(Romaji) : "[]");
        Assert.True(song!.IsSynced);
        Assert.StartsWith("kimi no koe", song.Lines[0].Text);
    }
}
