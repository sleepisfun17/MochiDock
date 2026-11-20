namespace MochiDock.Core;

public enum FxObservationType { Latest, DailyClose }
public sealed record FxQuote(DateTimeOffset SourceAt, DateTimeOffset FetchedAt, decimal Jpy, decimal Idr, DateOnly? HistoricalDate = null)
{
    public FxObservationType ObservationType => HistoricalDate.HasValue ? FxObservationType.DailyClose : FxObservationType.Latest;
    public decimal Rate(string pair) => pair switch { "USD/JPY" => Jpy, "USD/IDR" => Idr, "JPY/IDR" => Idr / Jpy, _ => throw new ArgumentException("Unknown pair") };
    public bool Valid
    {
        get { try { return Jpy > 0 && Idr > 0 && Idr / Jpy > 0; } catch (OverflowException) { return false; } }
    }
    public bool IsValidFor(DateOnly? requestedDate, DateTimeOffset now) => Valid && HistoricalDate == requestedDate
        && SourceAt > DateTimeOffset.UnixEpoch && SourceAt <= now.AddMinutes(5)
        && (!requestedDate.HasValue || DateOnly.FromDateTime(SourceAt.UtcDateTime) == requestedDate);
}
public sealed record FxPoint(DateTimeOffset At, FxQuote Quote);
public sealed record FxRange(decimal First, decimal Last, decimal Low, decimal High)
{
    public decimal Change => Last - First;
    public decimal Percent => FxMath.Percent(Last, First);
}
public static class FxMath
{
    public static decimal Change(decimal current, decimal previous) => current - previous;
    public static decimal Percent(decimal current, decimal previous) => previous > 0 ? (current / previous - 1) * 100 : throw new ArgumentOutOfRangeException(nameof(previous));
    public static int Precision(string pair) => pair == "USD/JPY" ? 3 : pair == "USD/IDR" ? 2 : 4;
    public static bool WithinBudget(IEnumerable<DateTimeOffset> ledger, DateTimeOffset now) => ledger.Count(t => t > now.AddDays(-31)) < 900;

    public static decimal? Calculate24hDelta(decimal currentValue, IEnumerable<(DateTimeOffset At, decimal Value)> bars, DateTimeOffset now)
    {
        if (currentValue <= 0) return null;
        var target = now.AddHours(-24);
        decimal? bestValue = null;
        double minDiff = double.MaxValue;
        foreach (var (at, val) in bars)
        {
            if (val <= 0) continue;
            var diff = Math.Abs((at - target).TotalSeconds);
            if (diff < minDiff)
            {
                minDiff = diff;
                bestValue = val;
            }
        }
        if (bestValue.HasValue && bestValue.Value > 0)
        {
            return (currentValue - bestValue.Value) / bestValue.Value * 100m;
        }
        return null;
    }

    public static FxPoint[] Series(IEnumerable<FxQuote> daily, IEnumerable<FxQuote> hourly, FxQuote? latest, bool useHourly, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (useHourly) return hourly.Where(q => q.Valid && q.HistoricalDate == null && q.SourceAt >= now.AddHours(-168) && q.SourceAt <= now)
            .DistinctBy(q => q.SourceAt).OrderBy(q => q.SourceAt).Select(q => new FxPoint(q.SourceAt, q)).ToArray();
        var points = daily.Where(q => q.Valid && q.HistoricalDate >= today.AddDays(-7) && q.HistoricalDate < today)
            .DistinctBy(q => q.HistoricalDate).Select(q => new FxPoint(new DateTimeOffset(q.HistoricalDate!.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), q)).ToList();
        // Keep a stale latest quote at its actual source time, never at the time it was checked.
        var start = new DateTimeOffset(today.AddDays(-7).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (latest is { Valid: true } && latest.SourceAt >= start && latest.SourceAt <= now) points.Add(new(latest.SourceAt, latest));
        return points.OrderBy(p => p.At).ToArray();
    }
    public static FxRange? Range(IEnumerable<FxPoint> points, string pair)
    {
        var values = points.Select(p => p.Quote.Rate(pair)).ToArray();
        return values.Length == 0 ? null : new(values[0], values[^1], values.Min(), values.Max());
    }
}
