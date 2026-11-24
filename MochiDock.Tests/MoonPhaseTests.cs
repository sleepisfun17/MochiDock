using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class MoonPhaseTests
{
    [Fact]
    public void EpochAndQuarterCycleFractionsMatchExpectedPhases()
    {
        var epoch = new DateTimeOffset(2000, 1, 6, 18, 14, 0, TimeSpan.Zero);

        var newMoon = MoonPhase.At(epoch);
        Assert.Equal("New Moon", newMoon.Name);
        Assert.InRange(newMoon.IlluminationPercent, 0, 1);
        Assert.True(newMoon.CycleFraction < 0.01);

        var firstQuarter = MoonPhase.At(epoch.AddDays(MoonPhase.SynodicMonthDays * 0.25));
        Assert.Equal("First Quarter", firstQuarter.Name);
        Assert.True(firstQuarter.IsWaxing);
        Assert.Equal(50, firstQuarter.IlluminationPercent);

        var fullMoon = MoonPhase.At(epoch.AddDays(MoonPhase.SynodicMonthDays * 0.5));
        Assert.Equal("Full Moon", fullMoon.Name);
        Assert.Equal(100, fullMoon.IlluminationPercent);

        var lastQuarter = MoonPhase.At(epoch.AddDays(MoonPhase.SynodicMonthDays * 0.75));
        Assert.Equal("Last Quarter", lastQuarter.Name);
        Assert.False(lastQuarter.IsWaxing);
        Assert.Equal(50, lastQuarter.IlluminationPercent);
    }

    [Fact]
    public void HandlesDatesBeforeReferenceEpochWithoutNegativeModulus()
    {
        var epoch = new DateTimeOffset(2000, 1, 6, 18, 14, 0, TimeSpan.Zero);
        var waxingGibbousBefore = MoonPhase.At(epoch.AddDays(-MoonPhase.SynodicMonthDays * 0.62));
        Assert.InRange(waxingGibbousBefore.CycleFraction, 0.0, 1.0);
        Assert.InRange(waxingGibbousBefore.AgeDays, 0.0, MoonPhase.SynodicMonthDays);
        Assert.Equal("Waxing Gibbous", waxingGibbousBefore.Name);
        Assert.Contains("Waxing Gibbous", waxingGibbousBefore.Summary);
        Assert.Contains("illuminated", waxingGibbousBefore.DetailTooltip);
    }
}
