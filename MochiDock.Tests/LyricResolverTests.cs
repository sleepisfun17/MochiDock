using MochiDock.Services;
using System.Net;
public class LyricResolverTests {
 sealed class Handler(Func<HttpRequestMessage,string> body):HttpMessageHandler {
  public List<string> Requests=new();
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t){Requests.Add(r.RequestUri!.Query);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body(r))});}
 }
 [Fact]public async Task PlainExactContinuesToSyncedAlbumless(){
  var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
  var h=new Handler(r=>r.RequestUri!.Query.Contains("album_name")?"""{"plainLyrics":"fallback"}""":"""{"duration":151,"syncedLyrics":"[00:01]one\n[00:02]two\n[00:03]three\n[00:04]four"}""");
  try{var s=new LrclibService(new HttpClient(h),dir);var song=await s.FetchLyricsAsync("IRIS OUT","Kenshi Yonezu","Album",151);Assert.True(song!.IsSynced);Assert.Equal(2,h.Requests.Count);Assert.DoesNotContain("album_name",h.Requests[1]);}finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
 [Fact]public async Task WrongDurationFallsBackToPlainAndRetryBypassesCache(){
  var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
  var h=new Handler(r=>r.RequestUri!.AbsolutePath.EndsWith("get")?"""{"duration":300,"plainLyrics":"fallback","syncedLyrics":"[00:01]one\n[00:02]two\n[00:03]three\n[00:04]four"}""":"[]");
  try{var s=new LrclibService(new HttpClient(h),dir);var song=await s.FetchLyricsAsync("Song","Artist","Album",151);Assert.NotNull(song);Assert.False(song.IsSynced);Assert.Equal("fallback",song.PlainLyrics);var count=h.Requests.Count;await s.FetchLyricsAsync("Song","Artist","Album",151);Assert.Equal(count,h.Requests.Count);await s.FetchLyricsAsync("Song","Artist","Album",151,forceRefresh:true);Assert.True(h.Requests.Count>count);}finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
}