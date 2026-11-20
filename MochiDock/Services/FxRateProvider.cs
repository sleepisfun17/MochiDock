using System.IO;
using System.Net.Http;
using System.Text.Json;
using MochiDock.Core;
namespace MochiDock.Services;

public interface IFxRateProvider
{
    Task<FxQuote> LatestAsync(string key, CancellationToken token);
    Task<FxQuote> HistoryAsync(string key, DateOnly date, CancellationToken token);
}
public sealed class FxProviderException(string message, TimeSpan retry, bool credentialError = false, bool historyDenied = false) : Exception(message)
{
    public TimeSpan Retry { get; } = retry;
    public bool CredentialError { get; } = credentialError;
    public bool HistoryDenied { get; } = historyDenied;
}
public sealed class OpenExchangeRatesProvider(HttpClient http, Func<DateTimeOffset>? clock = null) : IFxRateProvider
{
    public Task<FxQuote> LatestAsync(string key, CancellationToken token) => ReadAsync("latest.json", key, null, token);
    public Task<FxQuote> HistoryAsync(string key, DateOnly date, CancellationToken token) => ReadAsync($"historical/{date:yyyy-MM-dd}.json", key, date, token);
    async Task<FxQuote> ReadAsync(string route, string key, DateOnly? date, CancellationToken token)
    {
        using var response = await http.GetAsync("https://openexchangerates.org/api/" + route + "?app_id=" + Uri.EscapeDataString(key), token);
        var now = (clock ?? (() => DateTimeOffset.UtcNow))();
        var body = await response.Content.ReadAsStringAsync(token);
        JsonDocument? json = null;
        try { json = JsonDocument.Parse(body); }
        catch (JsonException) when (!response.IsSuccessStatusCode) { /* An HTTP error may contain an HTML proxy response. */ }
        using var document = json;
        var code = document?.RootElement.TryGetProperty("message", out var message) == true ? message.GetString() : null;
        if (!response.IsSuccessStatusCode || document?.RootElement.TryGetProperty("error", out var error) == true && error.ValueKind == JsonValueKind.True)
        {
            var invalidKey = response.StatusCode == System.Net.HttpStatusCode.Unauthorized || code is "invalid_app_id" or "missing_app_id";
            // Only the provider's explicit permission error means history is unsupported.
            // A generic 429/403 can instead mean quota exhaustion, and must back off all requests.
            var historyDenied = date.HasValue && code == "not_allowed";
            var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - now) ?? TimeSpan.FromMinutes(15);
            if (retry < TimeSpan.FromMinutes(1)) retry = TimeSpan.FromMinutes(1);
            var status = invalidKey ? "Invalid App ID — update it in Settings."
                : historyDenied ? "Daily history is unavailable for this account."
                : (int)response.StatusCode is 403 or 429 ? "Provider quota or access limit reached; requests paused."
                : "Provider request failed; cached rates retained.";
            throw new FxProviderException(status, retry, invalidKey, historyDenied);
        }
        var root = document?.RootElement ?? throw new InvalidDataException("Missing provider response.");
        if (root.GetProperty("base").GetString() != "USD") throw new InvalidDataException("Unexpected base currency.");
        var rates = root.GetProperty("rates");
        var quote = new FxQuote(DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("timestamp").GetInt64()), now, rates.GetProperty("JPY").GetDecimal(), rates.GetProperty("IDR").GetDecimal(), date);
        if (!quote.IsValidFor(date, now)) throw new InvalidDataException("Invalid provider rates or timestamp.");
        return quote;
    }
}
