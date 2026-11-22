using System.Net;
using MochiDock.Core;
using MochiDock.Models;
using MochiDock.Services;

public class FxPairPolicyTests
{
    [Fact] public void NullAndEmptyGiveTheDefaults()
    {
        Assert.Equal(FxPairPolicy.Defaults, FxPairPolicy.Normalize(null));
        Assert.Equal(FxPairPolicy.Defaults, FxPairPolicy.Normalize([]));
    }

    [Fact] public void InvalidDuplicateAndSameCurrencyPairsAreReplaced()
    {
        var result = FxPairPolicy.Normalize(["EUR/GBP", "EUR/GBP", "XXX/JPY", "USD/USD"]);
        Assert.Equal(3, result.Length);
        Assert.Equal("EUR/GBP", result[0]);
        Assert.Equal(3, result.Distinct().Count());
        Assert.All(result, p => Assert.True(FxPairPolicy.TryParse(p, out _, out _)));

        var same = FxPairPolicy.Normalize(["USD/USD", "garbage", ""]);
        Assert.Equal(FxPairPolicy.Defaults, same);
    }

    [Fact] public void LowerCaseIsAcceptedAndTooManyAreTrimmed()
    {
        Assert.Equal(["EUR/GBP", "SGD/IDR", "USD/JPY"], FxPairPolicy.Normalize(["eur/gbp", " sgd/idr ", "usd/jpy", "USD/IDR"]));
    }

    [Fact] public void OldSettingsJsonWithoutFxPairsLoadsWithTheDefaults()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{"ShowFxRatesTab":true}""")!;
        Assert.Equal(FxPairPolicy.Defaults, FxPairPolicy.Normalize(settings.FxPairs));
    }

    [Fact] public void CrossRatesUseUsdAsOne()
    {
        var rates = new Dictionary<string, decimal> { ["JPY"] = 150m, ["IDR"] = 15000m, ["EUR"] = 0.5m };
        Assert.Equal(150m, FxPairPolicy.CrossFromUsd(rates, "USD/JPY"));
        Assert.Equal(1m / 150m, FxPairPolicy.CrossFromUsd(rates, "JPY/USD"));
        Assert.Equal(100m, FxPairPolicy.CrossFromUsd(rates, "JPY/IDR"));
        Assert.Equal(30000m, FxPairPolicy.CrossFromUsd(rates, "EUR/IDR"));
        Assert.Null(FxPairPolicy.CrossFromUsd(rates, "USD/GBP"));
        Assert.Null(FxPairPolicy.CrossFromUsd(new Dictionary<string, decimal> { ["JPY"] = 0m }, "USD/JPY"));
    }

    [Fact] public void FormatUsesTheQuoteSymbolAndDecimals()
    {
        Assert.Equal("Rp 15,000", FxPairPolicy.Format(15000m, "USD/IDR"));
        Assert.Equal("Rp 106.25", FxPairPolicy.Format(106.25m, "JPY/IDR"));
        Assert.Equal("€1.08", FxPairPolicy.Format(1.0812m, "USD/EUR"));
        Assert.Equal("$0.0067", FxPairPolicy.Format(0.0067m, "JPY/USD"));
        Assert.Equal("¥150.12", FxPairPolicy.Format(150.123m, "USD/JPY"));
        Assert.Equal("$ → ¥", FxPairPolicy.Direction("USD/JPY"));
        Assert.Equal("Rp —", FxPairPolicy.Placeholder("USD/IDR"));
    }

    [Fact] public void YahooSymbolsAreBaseQuoteAndUnknownCodesThrow()
    {
        Assert.Equal("EURGBP=X", FxPairPolicy.YahooSymbol("EUR/GBP"));
        Assert.Equal("USDJPY=X", KeylessFxProvider.Symbol("usd/jpy"));
        Assert.Throws<ArgumentException>(() => FxPairPolicy.YahooSymbol("ABC/JPY"));
    }

    sealed class ErApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(request.RequestUri!.Host == "open.er-api.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"result":"success","base_code":"USD","time_last_update_unix":1793880000,"rates":{"USD":1,"EUR":0.5,"GBP":0.25,"SGD":2,"IDR":16000}}""") }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    [Fact] public async Task FallbackParsesCustomPairsFromOneUsdTable()
    {
        using var http = new HttpClient(new ErApiHandler());
        var provider = new KeylessFxProvider(http, () => DateTimeOffset.Parse("2026-11-06T12:00:00Z"));
        var quotes = await provider.Fallback(["EUR/GBP", "SGD/IDR", "USD/XYZ"], default);
        Assert.Equal(2, quotes.Length);
        Assert.Equal(0.5m, quotes.Single(q => q.Pair == "EUR/GBP").Value);
        Assert.Equal(8000m, quotes.Single(q => q.Pair == "SGD/IDR").Value);
        Assert.All(quotes, q => Assert.Equal("daily fallback", q.Interval));
    }

    // Yahoo has no VNDIDR=X (and similar): the card builds it from USD/VND and USD/IDR.
    [Fact] public void MissingCrossIsBuiltFromTheUsdLegs()
    {
        var t0 = DateTimeOffset.FromUnixTimeSeconds(100000);
        var usdVnd = new MarketQuote("USD/VND", "Yahoo Finance", 25000m, t0.AddHours(2), t0.AddHours(3),
            [new(t0, 25000m), new(t0.AddHours(1), 20000m)], "1h", PreviousClose: 25000m);
        var usdIdr = new MarketQuote("USD/IDR", "Yahoo Finance", 15000m, t0.AddHours(1), t0.AddHours(3),
            [new(t0.AddMinutes(-30), 15000m), new(t0.AddHours(1), 16000m)], "1h", PreviousClose: 12500m);
        var cross = KeylessFxProvider.Cross("VND/IDR", usdVnd, usdIdr, t0.AddHours(3));
        Assert.Equal(0.6m, cross.Value);
        Assert.Equal(t0.AddHours(1), cross.SourceAt);   // the older leg
        Assert.Equal("Yahoo Finance", cross.Provider);  // the card draws its chart
        Assert.Equal([0.8m], cross.Bars.Select(b => b.Value));   // the -30 min IDR bar has no VND bar at or before it
        Assert.Equal(0.5m, cross.PreviousClose);
        Assert.Equal(20m, cross.ChangePercent);

        var vndUsd = KeylessFxProvider.Cross("VND/USD", usdVnd, null, t0.AddHours(3));
        Assert.Equal(0.00004m, vndUsd.Value);
        Assert.Equal(2, vndUsd.Bars.Length);
    }
}
