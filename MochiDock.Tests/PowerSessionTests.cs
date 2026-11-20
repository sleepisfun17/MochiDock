using MochiDock.Core;
using MochiDock.Services;
using Xunit;

namespace MochiDock.Tests;

public class PowerSessionTests : IDisposable
{
    static readonly DateTimeOffset T0 = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
    readonly string folder = Path.Combine(Path.GetTempPath(), "mochidock-power-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(folder, "PowerSessions.json");

    public PowerSessionTests() => Directory.CreateDirectory(folder);
    public void Dispose() { try { Directory.Delete(folder, true); } catch { } }

    [Fact]
    public void ConstantPowerGivesEnergyAndAverage()
    {
        var t = new PowerSessionTracker(T0);
        for (var i = 0; i <= 360; i++) t.Add(T0.AddSeconds(i * 10), 100, PowerSource.Estimated); // one hour at 100 W
        var r = t.Snapshot(T0.AddHours(1), false);
        Assert.Equal(100, r.EnergyWh, 3);
        Assert.Equal(100, r.AverageW, 3);
        Assert.Equal(100, r.PeakW);
        Assert.Equal(3600, r.SampledSeconds, 3);
        Assert.Equal("Estimated", r.Source);
        Assert.False(r.Open);
    }

    [Fact]
    public void RampUsesTheTrapezoidRule()
    {
        var t = new PowerSessionTracker(T0);
        t.Add(T0, 100, PowerSource.Estimated);
        t.Add(T0.AddSeconds(30), 200, PowerSource.Estimated);
        Assert.Equal(150.0 * 30 / 3600, t.Snapshot(T0.AddSeconds(30), true).EnergyWh, 6);
        Assert.Equal(200, t.Snapshot(T0.AddSeconds(30), true).PeakW);
    }

    [Fact]
    public void LongGapsAreNotIntegrated()
    {
        var t = new PowerSessionTracker(T0);
        t.Add(T0, 100, PowerSource.Estimated);
        t.Add(T0.AddSeconds(10), 100, PowerSource.Estimated);
        t.Add(T0.AddHours(8), 100, PowerSource.Estimated); // slept 8 hours
        t.Add(T0.AddHours(8).AddSeconds(10), 100, PowerSource.Estimated);
        var r = t.Snapshot(T0.AddHours(8).AddSeconds(10), false);
        Assert.Equal(20, r.SampledSeconds, 3);
        Assert.Equal(100.0 * 20 / 3600, r.EnergyWh, 6);
    }

    [Fact]
    public void SourceIsMeasuredEstimatedOrMixed()
    {
        PowerSessionRecord Run(params PowerSource[] sources)
        {
            var t = new PowerSessionTracker(T0);
            t.Add(T0, 50, PowerSource.Measured);
            for (var i = 0; i < sources.Length; i++) t.Add(T0.AddSeconds((i + 1) * 10), 50, sources[i]);
            return t.Snapshot(T0.AddMinutes(1), false);
        }
        Assert.Equal("Measured", Run(PowerSource.Measured, PowerSource.Measured).Source);
        Assert.Equal("Estimated", Run(PowerSource.Estimated, PowerSource.Estimated).Source);
        Assert.Equal("Mixed", Run(PowerSource.Measured, PowerSource.Estimated).Source);
    }

    [Fact]
    public void NoSamplesMeansNoDataAndBadValuesAreIgnored()
    {
        var t = new PowerSessionTracker(T0);
        Assert.False(t.HasData);
        t.Add(T0, double.NaN, PowerSource.Estimated);
        t.Add(T0.AddSeconds(5), -3, PowerSource.Estimated);
        Assert.False(t.HasData);
        Assert.Equal(0, t.Snapshot(T0, true).AverageW);
    }

    [Fact]
    public void LaptopOnBatteryIsMeasuredEverythingElseIsEstimated()
    {
        var battery = PowerSampler.Choose(true, false, -14200, 40, 30, null, T0)!;
        Assert.Equal(PowerSource.Measured, battery.Source);
        Assert.Equal(14.2, battery.Watts, 3);

        var onAc = PowerSampler.Choose(true, true, 25000, 40, 30, null, T0)!;
        Assert.Equal(PowerSource.Estimated, onAc.Source);
        Assert.Equal(40 + 30 + PowerEstimator.DefaultAllowanceWatts, onAc.Watts);

        Assert.Equal(PowerSource.Estimated, PowerSampler.Choose(false, false, null, 40, null, null, T0)!.Source);
        Assert.Equal(PowerSource.Estimated, PowerSampler.Choose(true, false, BatteryRate.UnknownRate, 10, null, null, T0)!.Source);
        Assert.Null(PowerSampler.Choose(false, false, null, null, null, null, T0));
    }

    [Fact]
    public void HistoryReplacesBySameIdKeepsNewestFirstAndIsCapped()
    {
        var first = new PowerSessionRecord { Id = Guid.NewGuid(), EnergyWh = 1 };
        var list = PowerSessionFormat.AddToHistory(new List<PowerSessionRecord>(), first);
        var updated = new PowerSessionRecord { Id = first.Id, EnergyWh = 2 };
        list = PowerSessionFormat.AddToHistory(list, updated);
        Assert.Single(list);
        Assert.Equal(2, list[0].EnergyWh);
        for (var i = 0; i < PowerSessionFormat.HistoryCap + 5; i++) list = PowerSessionFormat.AddToHistory(list, new PowerSessionRecord { Id = Guid.NewGuid() });
        Assert.Equal(PowerSessionFormat.HistoryCap, list.Count);
    }

    [Fact]
    public void EnergyAndSummaryTexts()
    {
        Assert.Equal("85 Wh", PowerSessionFormat.Energy(85));
        Assert.Equal("0.18 kWh", PowerSessionFormat.Energy(180));
        Assert.Equal("0.61 kWh", PowerSessionFormat.Energy(610));
        Assert.Equal("1.50 kWh", PowerSessionFormat.Energy(1500));
        var start = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 18, 9, 0, 0)));
        var record = new PowerSessionRecord { StartUtc = start, EndUtc = start.AddHours(2), EnergyWh = 610, AverageW = 108, Source = "Estimated" };
        var text = PowerSessionFormat.Summary(record, new DateTime(2026, 11, 18, 20, 0, 0));
        Assert.Contains("Today", text);
        Assert.Contains("~0.61 kWh", text);
        Assert.Contains("avg ~108 W", text);
        Assert.Contains("Yesterday", PowerSessionFormat.Summary(record, new DateTime(2026, 11, 19, 8, 0, 0)));
        Assert.DoesNotContain("~", PowerSessionFormat.Summary(new PowerSessionRecord { StartUtc = start, EndUtc = start, Source = "Measured" }, new DateTime(2026, 11, 18, 20, 0, 0)));
    }

    [Fact]
    public async Task StoreRoundTripsAndClosesAnOpenRecordLeftByACrash()
    {
        var store = new PowerSessionStore(FilePath);
        var open = new PowerSessionRecord { Id = Guid.NewGuid(), StartUtc = T0, EndUtc = T0.AddMinutes(30), EnergyWh = 40, Open = true };
        store.Upsert(open);
        await store.FlushAsync();

        var reloaded = new PowerSessionStore(FilePath);
        var record = Assert.Single(reloaded.Sessions);
        Assert.Equal(open.Id, record.Id);
        Assert.False(record.Open);
        Assert.True(record.EndedUnexpectedly);
        Assert.Equal(40, record.EnergyWh);
        await reloaded.FlushAsync();
        Assert.True(Assert.Single(new PowerSessionStore(FilePath).Sessions).EndedUnexpectedly);
    }

    [Fact]
    public async Task StoreStartsEmptyOnUnreadableJsonAndClearEmptiesIt()
    {
        File.WriteAllText(FilePath, "{ not json");
        var store = new PowerSessionStore(FilePath);
        Assert.Empty(store.Sessions);
        store.Upsert(new PowerSessionRecord { Id = Guid.NewGuid() });
        store.Clear();
        await store.FlushAsync();
        Assert.Empty(new PowerSessionStore(FilePath).Sessions);
    }

    [Fact]
    public async Task LogServiceWritesNothingWhileOffOrBeforeAnySampleExisted()
    {
        var service = new PowerLogService(new PowerSessionStore(FilePath));
        service.Configure(false, () => null);
        Assert.Null(service.Current);
        await service.StopAsync();
        service.Configure(true, () => null); // nothing readable: no data, so no record
        await service.StopAsync();
        Assert.Empty(new PowerSessionStore(FilePath).Sessions);
        service.Dispose();
    }
}
