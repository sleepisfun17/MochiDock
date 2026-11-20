using MochiDock.Services;
using System.Net;
public class LyricsCacheTests {
 sealed class Handler : HttpMessageHandler {
  public int Calls; public string Body="[]"; public HttpStatusCode Status=HttpStatusCode.OK;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t) { Calls++; return Task.FromResult(new HttpResponseMessage(Status){Content=new StringContent(Body)}); }
 }
 [Fact] public async Task PersistsSuccessAndCoalescesQueuedLookups() {
  var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); var h=new Handler{Body="""[{"trackName":"Song","artistName":"Artist","plainLyrics":"Words"}]"""};
  try { var service=new LrclibService(new HttpClient(h),dir); var a=await service.FetchLyricsAsync("Song","Artist"); Assert.NotNull(a); var second=await service.FetchLyricsAsync("Song","Artist"); Assert.Equal(2,h.Calls);
   var next=new LrclibService(new HttpClient(h),dir); Assert.NotNull(await next.FetchLyricsAsync("Song","Artist")); Assert.Equal(2,h.Calls);
  } finally { if(Directory.Exists(dir)) Directory.Delete(dir,true); }
 }
 [Fact] public async Task EmptySearchIsCachedButServerErrorIsNot() {
  var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); var h=new Handler();
  try { Assert.Null(await new LrclibService(new HttpClient(h),dir).FetchLyricsAsync("Song","Artist")); Assert.Null(await new LrclibService(new HttpClient(h),dir).FetchLyricsAsync("Song","Artist")); Assert.Equal(2,h.Calls);
  h.Status=HttpStatusCode.InternalServerError; await new LrclibService(new HttpClient(h),dir).FetchLyricsAsync("Other","Artist"); await new LrclibService(new HttpClient(h),dir).FetchLyricsAsync("Other","Artist"); Assert.Equal(4,h.Calls);
  } finally { if(Directory.Exists(dir)) Directory.Delete(dir,true); }
 }
 [Fact] public async Task SyncedTextReturnsWithoutPronunciationAndExpiredCacheRefetches() {
  var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); var h=new Handler{Body="""[{"trackName":"Song","artistName":"Artist","syncedLyrics":"[00:01]one\n[00:02]two\n[00:03]three\n[00:04]four"}]"""};
  try { var first=await new LrclibService(new HttpClient(h),dir).FetchLyricsAsync("Song","Artist"); Assert.NotNull(first); Assert.True(first.IsSynced); Assert.All(first.Lines,l=>Assert.Empty(l.RubyPieces)); var path=Directory.GetFiles(dir,"*.json").Single(); var entry=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!; entry["Saved"]=DateTimeOffset.UtcNow.AddDays(-31); await File.WriteAllTextAsync(path,entry.ToJsonString()); Assert.NotNull(await new LrclibService(new HttpClient(h),dir).FetchLyricsAsync("Song","Artist")); Assert.Equal(2,h.Calls); }
  finally { if(Directory.Exists(dir))Directory.Delete(dir,true); }
 }
 [Fact] public async Task CancellationPropagates() { using var c=new CancellationTokenSource(); c.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new LrclibService(new HttpClient(new Handler()),Path.GetTempPath()).FetchLyricsAsync("Song","Artist",cancellationToken:c.Token)); }
}