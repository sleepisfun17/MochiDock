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

 sealed class DelayedHandler : HttpMessageHandler {
  public TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
  public string Body = """[{"trackName":"Song","artistName":"Artist","plainLyrics":"Shared"}]""";
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t) {
   await Gate.Task.WaitAsync(t);
   return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) };
  }
 }

 [Fact] public async Task CallerACancellingDoesNotAbortCallerBOnSharedFetch() {
  var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
  var h = new DelayedHandler();
  try {
   var service = new LrclibService(new HttpClient(h), dir);
   using var ctsA = new CancellationTokenSource();
   var taskA = service.FetchLyricsAsync("Song", "Artist", cancellationToken: ctsA.Token);
   var taskB = service.FetchLyricsAsync("Song", "Artist");
   ctsA.Cancel();
   await Assert.ThrowsAnyAsync<OperationCanceledException>(() => taskA);
   h.Gate.TrySetResult();
   var lyricsB = await taskB;
   Assert.NotNull(lyricsB);
   Assert.Equal("Shared", lyricsB.PlainLyrics);
  } finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
 }

 [Fact] public async Task ConcurrentForceRefreshWritesCacheSafely() {
  var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
  var h = new Handler { Body = """[{"trackName":"Song","artistName":"Artist","plainLyrics":"Words"}]""" };
  try {
   var service = new LrclibService(new HttpClient(h), dir);
   var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.FetchLyricsAsync("Song", "Artist", forceRefresh: true)));
   Assert.All(results, r => Assert.NotNull(r));
   Assert.Empty(Directory.GetFiles(dir, "*.tmp-*"));
  } finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
 }
}