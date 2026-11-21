using System.Globalization;

namespace MochiDock.Core;

/// <summary>A currency the FX card can show: ISO code, display symbol and the decimals its amounts use.</summary>
public sealed record FxCurrency(string Code, string Symbol, int Decimals)
{
    /// <summary>Picker text such as "JPY · ¥".</summary>
    public string Label => Code + " · " + Symbol;
}

/// <summary>
/// The three user-chosen currency pairs of the FX card: the curated currency list, normalization of the saved
/// setting, Yahoo symbols, display formatting and cross rates computed from a USD rate table.
/// </summary>
public static class FxPairPolicy
{
    public const int PairCount = 3;

    public static readonly IReadOnlyList<FxCurrency> Currencies =
    [
        new("USD", "$", 2), new("EUR", "€", 2), new("GBP", "£", 2), new("JPY", "¥", 2), new("CNY", "¥", 2),
        new("KRW", "₩", 0), new("IDR", "Rp", 0), new("SGD", "S$", 2), new("MYR", "RM", 2), new("THB", "฿", 2),
        new("PHP", "₱", 2), new("VND", "₫", 0), new("INR", "₹", 2), new("AUD", "A$", 2), new("NZD", "NZ$", 2),
        new("CAD", "C$", 2), new("CHF", "Fr", 2), new("HKD", "HK$", 2), new("TWD", "NT$", 2), new("SAR", "SR", 2),
        new("AED", "Dh", 2), new("TRY", "₺", 2), new("BRL", "R$", 2), new("MXN", "Mex$", 2), new("ZAR", "R", 2),
    ];

    /// <summary>The pairs a fresh install (and every user before this setting existed) shows.</summary>
    public static readonly IReadOnlyList<string> Defaults = ["USD/JPY", "USD/IDR", "JPY/IDR"];

    public static FxCurrency? Find(string? code) =>
        code is null ? null : Currencies.FirstOrDefault(c => string.Equals(c.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Splits "BASE/QUOTE" into upper-case codes when both are known and different.</summary>
    public static bool TryParse(string? pair, out string baseCode, out string quoteCode)
    {
        baseCode = quoteCode = "";
        if (string.IsNullOrWhiteSpace(pair)) return false;
        var parts = pair.Split('/');
        if (parts.Length != 2) return false;
        var b = Find(parts[0]); var q = Find(parts[1]);
        if (b is null || q is null || b.Code == q.Code) return false;
        baseCode = b.Code; quoteCode = q.Code;
        return true;
    }

    public static string Join(string baseCode, string quoteCode) => baseCode.ToUpperInvariant() + "/" + quoteCode.ToUpperInvariant();

    /// <summary>Always exactly three distinct, valid, upper-case pairs. Invalid, duplicate or missing slots are filled from <see cref="Defaults"/>.</summary>
    public static string[] Normalize(IEnumerable<string>? saved)
    {
        var result = new List<string?>(PairCount);
        var used = new HashSet<string>();
        foreach (var item in (saved ?? []).Take(PairCount))
        {
            if (TryParse(item, out var b, out var q) && used.Add(Join(b, q))) result.Add(Join(b, q));
            else result.Add(null);
        }
        while (result.Count < PairCount) result.Add(null);
        for (int i = 0; i < PairCount; i++)
        {
            if (result[i] is not null) continue;
            // At most two slots are filled when one is empty, so one of the three defaults is always free.
            var next = Defaults.First(d => !used.Contains(d));
            used.Add(next);
            result[i] = next;
        }
        return result.Select(p => p!).ToArray();
    }

    /// <summary>Yahoo's chart symbol, BASEQUOTE=X (it resolves for every pair). Throws for an unknown pair.</summary>
    public static string YahooSymbol(string pair) =>
        TryParse(pair, out var b, out var q) ? b + q + "=X" : throw new ArgumentException("Unknown pair");

    /// <summary>A rate with the quote currency's symbol: N2 (N0 for zero-decimal currencies at 1,000 or more), N4 when the value is below 1.</summary>
    public static string Format(decimal value, string pair)
    {
        if (!TryParse(pair, out _, out var quoteCode)) return value.ToString("N2", CultureInfo.InvariantCulture);
        var quote = Find(quoteCode)!;
        // Zero-decimal currencies drop decimals only for large values: JPY/IDR (about 106) keeps them, so small moves stay visible.
        var format = value < 1 ? "N4" : quote.Decimals == 0 && value >= 1000 ? "N0" : "N2";
        var space = char.IsLetter(quote.Symbol[^1]) ? " " : "";
        return quote.Symbol + space + value.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>The pair's placeholder before a rate arrives, for example "Rp —".</summary>
    public static string Placeholder(string pair)
    {
        if (!TryParse(pair, out _, out var quoteCode)) return "—";
        var quote = Find(quoteCode)!;
        return quote.Symbol + (char.IsLetter(quote.Symbol[^1]) ? " " : "") + "—";
    }

    /// <summary>For example "$ → ¥".</summary>
    public static string Direction(string pair) =>
        TryParse(pair, out var b, out var q) ? Find(b)!.Symbol + " → " + Find(q)!.Symbol : pair;

    /// <summary>Cross rate from a USD-based table (units of each currency per 1 USD; USD itself counts as 1), or null when a rate is missing or not positive.</summary>
    public static decimal? CrossFromUsd(IReadOnlyDictionary<string, decimal> usdRates, string pair)
    {
        if (!TryParse(pair, out var b, out var q)) return null;
        if (!Rate(usdRates, b, out var rb) || !Rate(usdRates, q, out var rq)) return null;
        return rq / rb;
    }

    static bool Rate(IReadOnlyDictionary<string, decimal> rates, string code, out decimal rate)
    {
        rate = 1m;
        if (code == "USD") return true;
        foreach (var (key, value) in rates)
            if (string.Equals(key, code, StringComparison.OrdinalIgnoreCase)) { rate = value; return value > 0; }
        return false;
    }
}
