using System.Net.Http;
using System.IO;
using System.Text.Json;
namespace MochiDock.Services;

public sealed record MarketBar(DateTimeOffset At, decimal Value);
public sealed record MarketQuote(string Pair, string Provider, decimal Value, DateTimeOffset SourceAt,
    DateTimeOffset FetchedAt, MarketBar[] Bars, string Interval, decimal? PreviousClose = null,
    decimal? ChangePercent = null);
public sealed class KeylessFxProvider(HttpClient http, Func<DateTimeOffset>? clock = null)
{
    readonly Dictionary<string, (int Failures, DateTimeOffset Until)> failures = new();
    DateTimeOffset Now => clock?.Invoke() ?? DateTimeOffset.UtcNow;
    public static string Symbol(string pair) => pair switch { "USD/JPY" => "USDJPY=X", "USD/IDR" => "USDIDR=X", "JPY/IDR" => "JPYIDR=X", _ => throw new ArgumentException("Unknown pair") };
    async Task<JsonDocument> Get(string provider, string url, CancellationToken token)
    {
        if (failures.TryGetValue(provider, out var prior) && Now < prior.Until) throw new HttpRequestException("Provider cooling down.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(AppUserAgent.Value);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? response.Headers.RetryAfter?.Date - Now ?? TimeSpan.Zero;
                var count = prior.Failures + 1;
                var backoff = TimeSpan.FromMinutes(Math.Min(120, 20 * Math.Pow(2, Math.Min(count - 1, 4))));
                failures[provider] = (count, Now + (delay > backoff ? delay : backoff));
                response.EnsureSuccessStatusCode();
            }
            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            failures.Remove(provider); return document;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            if (!failures.TryGetValue(provider, out var failure) || failure.Until <= Now)
            {
                var count = prior.Failures + 1;
                failures[provider] = (count, Now.AddMinutes(Math.Min(120, 20 * Math.Pow(2, Math.Min(count - 1, 4)))));
            }
            throw;
        }
    }
    public Task<MarketQuote> Yahoo(string pair, bool hourly, CancellationToken token) =>
        Yahoo(pair, hourly ? "7D" : "1D", token);

    public async Task<MarketQuote> Yahoo(string pair, string span, CancellationToken token)
    {
        string normSpan = span?.ToUpperInvariant() switch
        {
            "1M" or "1MO" => "1M",
            "1Y" => "1Y",
            "1D" => "1D",
            _ => "7D"
        };
        var (interval, range) = normSpan switch
        {
            "1M" => ("1h", "1mo"),   // hourly, like 7D: about 500 points instead of about 22 daily ones
            "1Y" => ("1d", "1y"),
            "1D" => ("1d", "1mo"),
            _ => ("1h", "7d")
        };
        using var json = await Get("Yahoo", $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(Symbol(pair))}?interval={interval}&range={range}", token);
        return ParseYahoo(json.RootElement, pair, interval, Now, normSpan);
    }
    public static MarketQuote ParseYahoo(JsonElement root, string pair, string interval, DateTimeOffset now, string span = "7D")
    {
        var result = root.GetProperty("chart").GetProperty("result")[0];
        var meta = result.GetProperty("meta");
        if (meta.GetProperty("symbol").GetString() != Symbol(pair)) throw new InvalidDataException("Unexpected FX symbol.");
        var value = meta.GetProperty("regularMarketPrice").GetDecimal();
        var source = DateTimeOffset.FromUnixTimeSeconds(meta.GetProperty("regularMarketTime").GetInt64());
        if (value <= 0 || source <= DateTimeOffset.UnixEpoch || source > now.AddMinutes(5)) throw new InvalidDataException("Invalid FX quote.");
        decimal? previousClose = null;
        decimal? changePercent = null;
        if (meta.TryGetProperty("chartPreviousClose", out var previousElement) &&
            previousElement.ValueKind == JsonValueKind.Number && previousElement.TryGetDecimal(out var previous) && previous > 0)
        {
            previousClose = previous;
            changePercent = (value - previous) / previous * 100m;
        }
        var bars = new List<MarketBar>();
        if (result.TryGetProperty("timestamp", out var times))
        {
            var closes = result.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close");
            var offset = meta.TryGetProperty("gmtoffset", out var zone) ? TimeSpan.FromSeconds(zone.GetInt32()) : TimeSpan.Zero;
            for (int i = 0; i < Math.Min(times.GetArrayLength(), closes.GetArrayLength()); i++)
            {
                if (closes[i].ValueKind != JsonValueKind.Number || times[i].ValueKind != JsonValueKind.Number || !closes[i].TryGetDecimal(out var v) || v <= 0 || !times[i].TryGetInt64(out var seconds)) continue;
                var at = DateTimeOffset.FromUnixTimeSeconds(seconds).ToOffset(offset);
                if (at > now || at <= DateTimeOffset.UnixEpoch) continue;
                if (interval == "1d" && at.Date >= now.ToOffset(offset).Date) continue;
                bars.Add(new(at, v));
            }
        }
        var ordered = bars.DistinctBy(b => b.At).OrderBy(b => b.At);
        MarketBar[] finalBars = (span == "1D" || (span == "7D" && interval == "1d"))
            ? ordered.TakeLast(7).ToArray()
            : ordered.ToArray();
        return new(pair, "Yahoo Finance", value, source, now,
            finalBars, interval, previousClose, changePercent);
    }
    public async Task<MarketQuote[]> Fallback(CancellationToken token)
    {
        foreach (var (provider, url) in new[] { ("ExchangeRate-API", "https://open.er-api.com/v6/latest/USD"), ("Fawaz", "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies/usd.json"), ("Fawaz mirror", "https://latest.currency-api.pages.dev/v1/currencies/usd.json") })
        {
            try
            {
                using var json = await Get(provider, url, token); var root = json.RootElement;
                var er = provider == "ExchangeRate-API";
                if (er && (root.GetProperty("result").GetString() != "success" || root.GetProperty("base_code").GetString() != "USD")) continue;
                var rates = root.GetProperty(er ? "rates" : "usd");
                var jpy = rates.GetProperty(er ? "JPY" : "jpy").GetDecimal(); var idr = rates.GetProperty(er ? "IDR" : "idr").GetDecimal();
                var source = er ? DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("time_last_update_unix").GetInt64()) : new DateTimeOffset(DateOnly.ParseExact(root.GetProperty("date").GetString()!, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                if (jpy <= 0 || idr <= 0 || source > Now.AddMinutes(5)) continue;
                return new[] { new MarketQuote("USD/JPY", provider,jpy,source,Now,[],"daily fallback"),new MarketQuote("USD/IDR",provider,idr,source,Now,[],"daily fallback"),new MarketQuote("JPY/IDR",provider,idr/jpy,source,Now,[],"daily fallback") };
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { }
        }
        throw new HttpRequestException("All FX sources unavailable.");
    }
}
