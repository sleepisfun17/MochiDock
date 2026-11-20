using System.Text.Json;
using MochiDock.Core;

public class StatsHistoryTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    static HistorySample S(double minutes, double cpu, double? cpuTemp = null, double? gpu = null, double? gpuTemp = null, double? ram = null) =>
        new(T0.AddMinutes(minutes), cpu, cpuTemp, gpu, gpuTemp, ram);

    [Fact]
    public void AMinuteAveragesLoadsAndKeepsTheHottestTemperature()
    {
        var h = new StatsHistory();
        h.Add(S(0.00, 10, 60));
        h.Add(S(0.25, 30, 80));
        h.Add(S(0.50, 20, null));   // an estimate is passed as null and never counts
        var b = Assert.Single(h.Buckets);
        Assert.Equal(20, b.CpuLoad!.Value, 6);
        Assert.Equal(70, b.CpuTemp!.Value, 6);
        Assert.Equal(80, b.CpuTempMax);
        Assert.Null(b.GpuTemp);
    }

    [Fact]
    public void TrimKeepsTwentyFourHours()
    {
        var h = new StatsHistory();
        for (int m = 0; m < 26 * 60; m += 30) h.Add(S(m, 5));
        h.Trim(T0.AddHours(26));
        Assert.All(h.Buckets, b => Assert.True(b.Start >= T0.AddHours(2) - TimeSpan.FromMinutes(1)));
        Assert.InRange(h.Count, 47, 49);
    }

    [Fact]
    public void GapsSplitTheLineAndXRunsAcrossTheSpan()
    {
        var h = new StatsHistory();
        for (int m = 0; m < 10; m++) h.Add(S(m, 50));
        for (int m = 30; m < 40; m++) h.Add(S(m, 100));   // 20 missing minutes: a new segment
        var now = T0.AddMinutes(60);
        var segments = h.Series(HistoryMetric.CpuLoad, now, TimeSpan.FromMinutes(60), 220, 100, 100);
        Assert.Equal(2, segments.Count);
        Assert.Equal(0, segments[0][0].X, 6);                      // 60 minutes ago is the left edge
        Assert.Equal(220.0 * 39 / 60, segments[1][^1].X, 6);
        Assert.Equal(100 - 0.5 * 97, segments[0][0].Y, 6);
        Assert.Equal(3, segments[1][0].Y, 6);                       // 100 % sits 3 below the top, like the 2-minute charts
    }

    [Fact]
    public void PeakFindsTheHottestMinuteInTheSpan()
    {
        var h = new StatsHistory();
        h.Add(S(0, 10, gpuTemp: 70));
        h.Add(S(5, 10, gpuTemp: 86));
        h.Add(S(9, 10, gpuTemp: 75));
        var peak = h.Peak(HistoryMetric.GpuTemp, T0.AddMinutes(10), TimeSpan.FromHours(24));
        Assert.Equal((86d, T0.AddMinutes(5)), peak);
        Assert.Null(h.Peak(HistoryMetric.CpuTemp, T0.AddMinutes(10), TimeSpan.FromHours(24)));
    }

    [Fact]
    public void BucketsRoundTripThroughJson()
    {
        var h = new StatsHistory();
        h.Add(S(0, 12, 55, 40, 66, 70));
        h.Add(S(0.5, 14, 57, 42, 68, 71));
        string json = JsonSerializer.Serialize(h.Buckets);
        var back = new StatsHistory(JsonSerializer.Deserialize<List<HistoryBucket>>(json));
        back.Add(S(0.75, 16));   // folding continues exactly after a reload
        Assert.Equal(14, back.Buckets[0].CpuLoad!.Value, 6);
        Assert.Equal(68, back.Buckets[0].GpuTempMax);
    }
}

public class StatsHistoryStoreTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "MochiDock.Tests", "history-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(folder, "StatsHistory.json");
    public StatsHistoryStoreTests() => Directory.CreateDirectory(folder);
    public void Dispose() { try { Directory.Delete(folder, true); } catch (IOException) { } }

    [Fact]
    public async Task SavesAndReloadsTheLastDay()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new MochiDock.Services.StatsHistoryService(FilePath);
        store.Add(new HistorySample(now.AddMinutes(-2), 20, 60, 30, 70, 50));
        store.Add(new HistorySample(now.AddMinutes(-1), 40, 62, 35, 72, 51));
        await store.FlushAsync();
        var reloaded = new MochiDock.Services.StatsHistoryService(FilePath);
        Assert.Equal(2, reloaded.Count);
        Assert.Equal(72d, reloaded.Read(h => h.Peak(HistoryMetric.GpuTemp, now, TimeSpan.FromHours(24)))!.Value.Value);
        reloaded.Clear();
        await reloaded.FlushAsync();
        Assert.Equal(0, new MochiDock.Services.StatsHistoryService(FilePath).Count);
    }

    [Fact]
    public void AnUnreadableFileStartsEmpty()
    {
        File.WriteAllText(FilePath, "{ not json");
        Assert.Equal(0, new MochiDock.Services.StatsHistoryService(FilePath).Count);
    }
}

public class SustainedHeatPolicyTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 21, 20, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan Ten = TimeSpan.FromMinutes(10);

    [Fact]
    public void FiresOnceAfterTheHoldTime()
    {
        var p = new SustainedHeatPolicy();
        bool fired = false;
        for (int s = 0; s <= 600; s += 15)
        {
            bool now = p.Evaluate("GPU", 82, 80, Ten, T0.AddSeconds(s));
            if (s < 600) Assert.False(now); else fired = now;
        }
        Assert.True(fired);
        Assert.True(p.IsHot("GPU"));
        Assert.True(p.IsSustained("GPU", Ten, T0.AddSeconds(600)));
        Assert.False(p.IsSustained("GPU", Ten, T0.AddSeconds(599)));
        Assert.False(p.Evaluate("GPU", 85, 80, Ten, T0.AddSeconds(615)));   // the same spell: once
    }

    [Fact]
    public void AGapOrADipRestartsTheCount()
    {
        var p = new SustainedHeatPolicy();
        p.Evaluate("CPU", 85, 80, Ten, T0);
        Assert.False(p.Evaluate("CPU", 85, 80, Ten, T0.AddMinutes(5)));      // 5-minute gap: counting again from here
        for (int m = 6; m <= 14; m++) Assert.False(p.Evaluate("CPU", 85, 80, Ten, T0.AddMinutes(m)));
        Assert.True(p.Evaluate("CPU", 85, 80, Ten, T0.AddMinutes(15)));
        var q = new SustainedHeatPolicy();
        q.Evaluate("CPU", 85, 80, Ten, T0);
        q.Evaluate("CPU", 79, 80, Ten, T0.AddMinutes(1));
        Assert.False(q.Evaluate("CPU", 85, 80, Ten, T0.AddMinutes(10.5)));
    }

    [Fact]
    public void RearmsOnlyAfterCoolingDown()
    {
        var p = new SustainedHeatPolicy();
        for (int m = 0; m <= 10; m++) p.Evaluate("GPU", 85, 80, Ten, T0.AddMinutes(m));
        for (int m = 11; m <= 12; m++) p.Evaluate("GPU", 77, 80, Ten, T0.AddMinutes(m));   // only 3 under: not cool enough
        for (int m = 13; m <= 23; m++) Assert.False(p.Evaluate("GPU", 85, 80, Ten, T0.AddMinutes(m)));
        for (int m = 24; m <= 26; m++) p.Evaluate("GPU", 70, 80, Ten, T0.AddMinutes(m));   // 2 minutes well under: re-armed
        bool fired = false;
        for (int m = 27; m <= 37; m++) fired |= p.Evaluate("GPU", 85, 80, Ten, T0.AddMinutes(m));
        Assert.True(fired);
    }

    [Fact]
    public void UnmeasuredReadingsNeverCount()
    {
        var p = new SustainedHeatPolicy();
        for (int m = 0; m <= 20; m++) Assert.False(p.Evaluate("CPU", null, 80, Ten, T0.AddMinutes(m)));
        Assert.False(p.IsHot("CPU"));
    }
}
