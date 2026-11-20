using System.Net;
using System.Text.Json;
using MochiDock.Services;
using MochiDock.Core;
public class KeylessFxTests
{
    [Fact] public void TransitionHasOverlappingFadesAndSettles()
    {
        Assert.Equal((1d,0d),TabMotion.Opacity(0));
        Assert.True(TabMotion.Opacity(70).Incoming > 0);
        Assert.True(TabMotion.Opacity(70).Outgoing > 0);
        Assert.Equal((0d,1d),TabMotion.Opacity(260));
        Assert.Equal(-1,TabMotion.Direction(4,1));Assert.Equal(0,TabMotion.Direction(-1,1));
    }
    [Theory] [InlineData("USD/JPY","USDJPY=X")] [InlineData("USD/IDR","USDIDR=X")] [InlineData("JPY/IDR","JPYIDR=X")]
    public void Symbols(string pair,string symbol)=>Assert.Equal(symbol,KeylessFxProvider.Symbol(pair));
    [Fact] public void ParsesBarsWithoutRoundingAndSkipsNulls()
    {
        using var json=JsonDocument.Parse("""{"chart":{"result":[{"meta":{"symbol":"USDJPY=X","regularMarketPrice":150.123456,"regularMarketTime":100000,"gmtoffset":0},"timestamp":[90000,91000,92000],"indicators":{"quote":[{"close":[150.123456,null,-1]}]}}]}}""");
        var q=KeylessFxProvider.ParseYahoo(json.RootElement,"USD/JPY","1h",DateTimeOffset.FromUnixTimeSeconds(110000));
        Assert.Equal(150.123456m,q.Value);Assert.Single(q.Bars);Assert.Equal(150.123456m,q.Bars[0].Value);
        Assert.Null(q.PreviousClose);Assert.Null(q.ChangePercent);
        Assert.Throws<InvalidDataException>(()=>KeylessFxProvider.ParseYahoo(json.RootElement,"USD/IDR","1h",DateTimeOffset.FromUnixTimeSeconds(110000)));
    }
    [Theory]
    [InlineData("150", "153", "2")]
    [InlineData("160", "152", "-5")]
    public void ParsesProviderPreviousCloseAndChangeWithoutRounding(string previousText,string valueText,string expectedText)
    {
        const string template="""{"chart":{"result":[{"meta":{"symbol":"USDJPY=X","regularMarketPrice":VALUE,"regularMarketTime":100000,"chartPreviousClose":PREVIOUS,"gmtoffset":0},"timestamp":[],"indicators":{"quote":[{"close":[]}]}}]}}""";
        using var json=JsonDocument.Parse(template.Replace("VALUE",valueText).Replace("PREVIOUS",previousText));
        var quote=KeylessFxProvider.ParseYahoo(json.RootElement,"USD/JPY","1h",DateTimeOffset.FromUnixTimeSeconds(110000));
        Assert.Equal(decimal.Parse(previousText),quote.PreviousClose);
        Assert.Equal(decimal.Parse(expectedText),quote.ChangePercent);
    }
    [Theory] [InlineData("0")] [InlineData("-1")]
    public void InvalidPreviousCloseLeavesComparisonUnavailable(string previousText)
    {
        const string template="""{"chart":{"result":[{"meta":{"symbol":"USDJPY=X","regularMarketPrice":153,"regularMarketTime":100000,"chartPreviousClose":PREVIOUS,"gmtoffset":0},"timestamp":[],"indicators":{"quote":[{"close":[]}]}}]}}""";
        using var json=JsonDocument.Parse(template.Replace("PREVIOUS",previousText));
        var quote=KeylessFxProvider.ParseYahoo(json.RootElement,"USD/JPY","1h",DateTimeOffset.FromUnixTimeSeconds(110000));
        Assert.Null(quote.PreviousClose);Assert.Null(quote.ChangePercent);
    }
    [Fact] public void LegacyCachedMarketQuoteKeepsNewComparisonFieldsUnavailable()
    {
        var quote=JsonSerializer.Deserialize<MarketQuote>("""{"Pair":"USD/JPY","Provider":"Yahoo Finance","Value":150.5,"SourceAt":"2026-11-06T10:00:00Z","FetchedAt":"2026-11-06T10:01:00Z","Bars":[],"Interval":"1h"}""");
        Assert.NotNull(quote);Assert.Null(quote.PreviousClose);Assert.Null(quote.ChangePercent);
    }
    sealed class Handler:HttpMessageHandler
    {
        public List<string> Hosts=new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Hosts.Add(request.RequestUri!.Host);
            return Task.FromResult(request.RequestUri.Host=="open.er-api.com"?new HttpResponseMessage(HttpStatusCode.ServiceUnavailable):new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("""{"date":"2026-11-06","usd":{"jpy":150,"idr":15000}}""")});
        }
    }
    [Fact] public async Task FallbackIsKeylessAndCrossRateUsesOneSnapshot()
    {
        var handler=new Handler();using var http=new HttpClient(handler);
        var provider=new KeylessFxProvider(http,()=>DateTimeOffset.Parse("2026-11-06T12:00:00Z"));
        var quotes=await provider.Fallback(default);
        Assert.Equal(new[]{"open.er-api.com","cdn.jsdelivr.net"},handler.Hosts);
        Assert.Equal(100m,quotes.Single(q=>q.Pair=="JPY/IDR").Value);
        Assert.All(quotes,q=>{Assert.Empty(q.Bars);Assert.Equal("daily fallback",q.Interval);Assert.Null(q.PreviousClose);Assert.Null(q.ChangePercent);});
    }
    [Fact] public async Task ServiceStartsWithoutCredentialsAndRetainsCacheWhenDisabled()
    {
        var handler=new Handler();using var http=new HttpClient(handler);
        var now=DateTimeOffset.Parse("2026-11-06T12:00:00Z");
        var directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        var service=new FxRatesService(new KeylessFxProvider(http,()=>now),directory,()=>now);
        try {
            Assert.Equal("JPY/IDR",service.SelectedPair);Assert.True(service.Hourly);
            await service.StartAsync(true,false);await service.RefreshAsync();
            Assert.Equal(3,service.Markets.Count);
            Assert.Equal(100m,service.Markets["JPY/IDR"].Value);
            var calls=handler.Hosts.Count;service.Configure(false,false);await service.RefreshAsync();
            Assert.Equal(calls,handler.Hosts.Count);Assert.Equal(3,service.Markets.Count);
        }
        finally {await service.StopAsync();if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [Fact] public async Task ManualRefreshCooldownDoesNotSendAnotherRequest()
    {
        var handler=new Handler();using var http=new HttpClient(handler);
        var now=DateTimeOffset.Parse("2026-11-06T12:00:00Z");
        var directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        var service=new FxRatesService(new KeylessFxProvider(http,()=>now),directory,()=>now);
        try {
            await service.StartAsync(true,false);await service.RefreshAsync(true);
            var calls=handler.Hosts.Count;await service.RefreshAsync(true);
            Assert.Equal(calls,handler.Hosts.Count);Assert.Contains("every minute",service.State.Status);
        }
        finally {await service.StopAsync();if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }

    [Fact]
    public void ParsesBarsFor1MAnd1YSpansWithoutTruncatingTo7()
    {
        // 12 daily bars
        var timestamps = Enumerable.Range(1, 12).Select(i => (long)i * 86400).ToArray();
        var closes = Enumerable.Range(1, 12).Select(i => (decimal)150 + i).ToArray();
        var timeJson = string.Join(",", timestamps);
        var closeJson = string.Join(",", closes);

        using var json = JsonDocument.Parse($$"""
        {
          "chart": {
            "result": [
              {
                "meta": { "symbol": "USDJPY=X", "regularMarketPrice": 162.0, "regularMarketTime": 1200000, "gmtoffset": 0 },
                "timestamp": [{{timeJson}}],
                "indicators": { "quote": [ { "close": [{{closeJson}}] } ] }
              }
            ]
          }
        }
        """);

        var qLegacy = KeylessFxProvider.ParseYahoo(json.RootElement, "USD/JPY", "1d", DateTimeOffset.FromUnixTimeSeconds(2000000), "1D");
        Assert.Equal(7, qLegacy.Bars.Length); // 1D legacy truncates to 7

        var qMonth = KeylessFxProvider.ParseYahoo(json.RootElement, "USD/JPY", "1d", DateTimeOffset.FromUnixTimeSeconds(2000000), "1M");
        Assert.Equal(12, qMonth.Bars.Length); // 1M keeps all 12 bars

        var qYear = KeylessFxProvider.ParseYahoo(json.RootElement, "USD/JPY", "1d", DateTimeOffset.FromUnixTimeSeconds(2000000), "1Y");
        Assert.Equal(12, qYear.Bars.Length); // 1Y keeps all 12 bars
    }

    [Fact]
    public async Task SelectSpanUpdatesActiveSpan()
    {
        var handler = new Handler(); using var http = new HttpClient(handler);
        var now = DateTimeOffset.Parse("2026-11-06T12:00:00Z");
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new FxRatesService(new KeylessFxProvider(http, () => now), directory, () => now);
        try
        {
            Assert.Equal("7D", service.SelectedSpan);
            Assert.True(service.Hourly);
            service.SelectSpan("1Y");
            Assert.Equal("1Y", service.SelectedSpan);
            Assert.False(service.Hourly);
            service.SelectSpan("1M");
            Assert.Equal("1M", service.SelectedSpan);
        }
        finally
        {
            // Wait for the background refresh and its save to finish before deleting the folder they write to.
            await service.StopAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
