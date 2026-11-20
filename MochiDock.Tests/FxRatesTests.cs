using MochiDock.Core;
using MochiDock.Services;
using System.Net;
public class FxRatesTests {
 sealed class Keys:IFxCredentialStore {public string? Key="fake";public string? Read()=>Key;public void Save(string key)=>Key=key;public void Remove()=>Key=null;}
 sealed class Provider(Func<DateTimeOffset> now):IFxRateProvider {
  public int Latest,History;public bool DenyHistory;
  public Task<FxQuote> LatestAsync(string key,CancellationToken t){Latest++;return Task.FromResult(new FxQuote(now(),now(),150m,15000m));}
  public Task<FxQuote> HistoryAsync(string key,DateOnly date,CancellationToken t){History++;if(DenyHistory)throw new FxProviderException("Denied",TimeSpan.FromMinutes(15),historyDenied:true);return Task.FromResult(new FxQuote(new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue),TimeSpan.Zero),now(),149m,14900m,date));}
 }
 [Fact]public void CrossRatePrecisionAndChange(){var q=new FxQuote(DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,150m,15000m);Assert.Equal(100m,q.Rate("JPY/IDR"));Assert.Equal(150m,q.Rate("USD/JPY"));Assert.Equal(15000m,q.Rate("USD/IDR"));Assert.Equal(10m,FxMath.Percent(110,100));Assert.Equal(3,FxMath.Precision("USD/JPY"));Assert.False(FxMath.WithinBudget(Enumerable.Repeat(DateTimeOffset.UtcNow,900),DateTimeOffset.UtcNow));}
 [Fact]public async Task HourlyRefreshBackfillsHistoryAndThrottlesManual(){
  var now=DateTimeOffset.Parse("2026-11-06T12:00:00Z");var provider=new Provider(()=>now);var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
  using var service=new FxRatesService(provider,new Keys(),dir,()=>now);
  try{await service.StartAsync(true,false);await service.RefreshAsync();Assert.Equal(1,provider.Latest);Assert.Equal(7,provider.History);Assert.Equal(7,service.State.Daily.Length);await service.RefreshAsync();Assert.Equal(1,provider.Latest);await service.RefreshAsync(true);Assert.Equal(2,provider.Latest);Assert.Single(service.State.Hourly);await service.RefreshAsync(true);Assert.Equal(2,provider.Latest);now=now.AddHours(1);await service.RefreshAsync();Assert.Equal(3,provider.Latest);Assert.Equal(7,provider.History);}finally{Directory.Delete(dir,true);}
 }
 [Fact]public async Task MissingKeyAndHistoryDenialAreExplicit(){
  var now=DateTimeOffset.UtcNow;var p=new Provider(()=>now){DenyHistory=true};var keys=new Keys{Key=null};var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));using var s=new FxRatesService(p,keys,dir,()=>now);
  try{await s.StartAsync(true,false);await s.RefreshAsync();Assert.Equal(0,p.Latest);Assert.Contains("Missing key",s.State.Status);keys.Key="fake";await s.RefreshAsync();Assert.NotNull(s.State.Latest);Assert.Contains("unavailable",s.State.HistoryError);Assert.Equal(1,p.History);s.Configure(true,true);await s.RefreshAsync(true);Assert.Equal(1,p.Latest);}finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
 sealed class Handler(string body,HttpStatusCode status=HttpStatusCode.OK):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(body)});}
 [Fact]public async Task ProviderValidatesQuotesAndReportsInvalidKey(){var now=DateTimeOffset.UtcNow;var json=System.Text.Json.JsonSerializer.Serialize(new { @base="USD",timestamp=now.ToUnixTimeSeconds(),rates=new {JPY=150m,IDR=15000m}});var provider=new OpenExchangeRatesProvider(new HttpClient(new Handler(json)),()=>now);Assert.Equal(100m,(await provider.LatestAsync("fake",default)).Rate("JPY/IDR"));var error=new OpenExchangeRatesProvider(new HttpClient(new Handler("""{"error":true}""",HttpStatusCode.Unauthorized)));var ex=await Assert.ThrowsAsync<FxProviderException>(()=>error.LatestAsync("fake",default));Assert.True(ex.CredentialError);}

 [Fact]public async Task BudgetStopsRequestsAndKeepsCachedData(){var now=DateTimeOffset.UtcNow;var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));var quote=new FxQuote(now.AddHours(-3),now,150,15000);AtomicJsonStore.Save(Path.Combine(dir,"rates.json"),new FxCache{Latest=quote,Requests=Enumerable.Repeat(now,900).ToList()});using var service=new FxRatesService(new Provider(()=>now),new Keys(),dir,()=>now);try{await service.StartAsync(true,false);await service.RefreshAsync();Assert.Contains("budget",service.State.Status);Assert.Equal(quote,service.State.Latest);}finally{Directory.Delete(dir,true);}}
}