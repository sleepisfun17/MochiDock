using System.Text.Json;
using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class PowerComponentsTests
{
    static PowerComponent Item(string id, PowerComponentKind kind, double watts, int count = 1, bool detected = true, bool enabled = true, bool edited = false) =>
        new() { Id = id, Name = id, Kind = kind, Watts = watts, Count = count, Detected = detected, Enabled = enabled, Edited = edited };

    [Fact]
    public void EmptyListFallsBackToTheFixedAllowance()
    {
        Assert.Equal(PowerEstimator.DefaultAllowanceWatts, PowerAllowance.Total(null));
        Assert.Equal(PowerEstimator.DefaultAllowanceWatts, PowerAllowance.Total(new List<PowerComponent>()));
    }

    [Fact]
    public void TotalSumsWattsTimesCountAndSkipsSwitchedOffItems()
    {
        var list = new List<PowerComponent>
        {
            Item("board", PowerComponentKind.Motherboard, 20),
            Item("ram", PowerComponentKind.Memory, 4, count: 2),
            Item("hdd", PowerComponentKind.Hdd35, 7, enabled: false),
        };
        Assert.Equal(28, PowerAllowance.Total(list));
        Assert.Equal(0, PowerAllowance.Total(new List<PowerComponent> { Item("x", PowerComponentKind.Other, 5, enabled: false) }));
    }

    [Fact]
    public void ItemsAreClampedSoATypoCannotDominate()
    {
        Assert.Equal(PowerAllowance.MaxItemWatts, PowerAllowance.ItemWatts(Item("a", PowerComponentKind.Other, 50000)));
        Assert.Equal(PowerAllowance.MaxItemWatts, PowerAllowance.ItemWatts(Item("b", PowerComponentKind.Fan, 300, count: 99)));
        Assert.Equal(0, PowerAllowance.ItemWatts(Item("c", PowerComponentKind.Fan, -4)));
        Assert.Equal(0, PowerAllowance.ItemWatts(Item("d", PowerComponentKind.Fan, double.NaN)));
    }

    [Fact]
    public void DescribeGroupsByKind()
    {
        var text = PowerAllowance.Describe(new List<PowerComponent>
        {
            Item("board", PowerComponentKind.Motherboard, 20),
            Item("r1", PowerComponentKind.Memory, 4),
            Item("r2", PowerComponentKind.Memory, 4),
        });
        Assert.Equal("board 20 + memory 8 = 28 W", text);
        Assert.Contains("assumed", PowerAllowance.Describe(null));
    }

    [Fact]
    public void MergeKeepsEditsAddsNewDropsGoneAndKeepsUserItems()
    {
        var saved = new List<PowerComponent>
        {
            Item("disk:0", PowerComponentKind.Nvme, 9, edited: true),     // user raised it
            Item("disk:1", PowerComponentKind.Hdd35, 7),                   // drive since removed
            Item("user:fans", PowerComponentKind.Fan, 2, count: 5, detected: false),
        };
        var scan = new List<PowerComponent>
        {
            Item("disk:0", PowerComponentKind.Nvme, 5),
            Item("disk:2", PowerComponentKind.SataSsd, 2.5),
        };
        var merged = PowerComponentMerge.Merge(saved, scan);
        Assert.Equal(9, merged.Single(m => m.Id == "disk:0").Watts);
        Assert.Contains(merged, m => m.Id == "disk:2");
        Assert.DoesNotContain(merged, m => m.Id == "disk:1");
        Assert.Equal(5, merged.Single(m => m.Id == "user:fans").Count);
    }

    [Fact]
    public void MergeRefreshesWattsOfUneditedItems()
    {
        var merged = PowerComponentMerge.Merge(new[] { Item("disk:0", PowerComponentKind.Hdd25, 2) }, new[] { Item("disk:0", PowerComponentKind.Hdd35, 7) });
        var item = Assert.Single(merged);
        Assert.Equal(PowerComponentKind.Hdd35, item.Kind);
        Assert.Equal(7, item.Watts);
    }

    [Fact]
    public void DefaultsAreSane()
    {
        foreach (var kind in Enum.GetValues<PowerComponentKind>())
        {
            Assert.InRange(PowerComponentDefaults.Watts(kind), 0.5, 30);
            Assert.False(string.IsNullOrWhiteSpace(PowerComponentDefaults.Label(kind)));
        }
        Assert.True(PowerComponentDefaults.Watts(PowerComponentKind.Memory, true) > PowerComponentDefaults.Watts(PowerComponentKind.Memory, false));
    }

    [Fact]
    public void ComponentListSurvivesJsonAndOldFilesWithoutItStillLoad()
    {
        var json = JsonSerializer.Serialize(new List<PowerComponent> { Item("board", PowerComponentKind.Motherboard, 20, count: 1) });
        var back = JsonSerializer.Deserialize<List<PowerComponent>>(json)!;
        Assert.Equal(20, back[0].Watts);
        Assert.Equal(PowerComponentKind.Motherboard, back[0].Kind);
        Assert.Empty(JsonSerializer.Deserialize<List<PowerComponent>>("[]")!);
    }

    [Fact]
    public void EstimatorUsesTheGivenAllowanceAndNote()
    {
        var r = PowerEstimator.Estimate(40, 60, null, DateTimeOffset.UnixEpoch, 28, "board 20 + memory 8 = 28 W")!;
        Assert.Equal(128, r.Watts);
        Assert.Contains("28 other", r.Detail);
        Assert.Contains("board 20", r.Detail);
    }
}
