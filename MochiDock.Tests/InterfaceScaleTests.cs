using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class InterfaceScaleTests
{
    [Theory]
    [InlineData(0, 80)][InlineData(79, 80)][InlineData(80, 80)][InlineData(133, 133)][InlineData(200, 200)][InlineData(500, 200)][InlineData(-5, 80)]
    public void ClampKeepsTheChosenRange(int input, int expected) => Assert.Equal(expected, InterfaceScale.Clamp(input));

    [Fact]
    public void PresetsAreSortedAndWithinTheRange()
    {
        Assert.Equal(InterfaceScale.Presets.OrderBy(p => p), InterfaceScale.Presets);
        Assert.Equal(InterfaceScale.Min, InterfaceScale.Presets[0]);
        Assert.Equal(InterfaceScale.Max, InterfaceScale.Presets[^1]);
        Assert.Contains(InterfaceScale.Default, InterfaceScale.Presets);
    }

    [Theory]
    [InlineData(100, 110)][InlineData(110, 125)][InlineData(125, 150)][InlineData(200, 200)][InlineData(80, 90)][InlineData(133, 150)][InlineData(5, 90)]
    public void NextStepsUpAndStopsAtTheLargest(int from, int expected) => Assert.Equal(expected, InterfaceScale.Next(from));

    [Theory]
    [InlineData(100, 90)][InlineData(90, 80)][InlineData(80, 80)][InlineData(150, 125)][InlineData(133, 125)][InlineData(999, 175)]
    public void PreviousStepsDownAndStopsAtTheSmallest(int from, int expected) => Assert.Equal(expected, InterfaceScale.Previous(from));

    [Fact]
    public void ALargeScreenKeepsTheRequestedSize()
    {
        var r = InterfaceScale.Effective(150, 1920, 1040, 680, 450, 4);
        Assert.Equal(new InterfaceScaleResult(150, 1.5, false), r);
        Assert.Equal(200, InterfaceScale.Effective(200, 2560, 1400, 680, 450, 4).Percent);
    }

    [Fact]
    public void ASmallLaptopScreenLimitsTheSize()
    {
        // 1366x768 at 100% Windows scaling: about 728 DIPs of work area height. 450 * s + 4 <= 728 -> at most 160 %.
        var r = InterfaceScale.Effective(200, 1366, 728, 680, 450, 4);
        Assert.True(r.Limited);
        Assert.Equal(160, r.Percent);
        Assert.Equal(1.6, r.Scale, 3);
        Assert.False(InterfaceScale.Effective(150, 1366, 728, 680, 450, 4).Limited);
    }

    [Fact]
    public void WidthCanLimitTooAndTheResultNeverGoesBelowTheFloor()
    {
        Assert.Equal(147, InterfaceScale.Effective(200, 1000, 2000, 680, 450).Percent); // 1000 / 680
        var tiny = InterfaceScale.Effective(200, 300, 200, 680, 450);
        Assert.Equal(InterfaceScale.FitFloor, tiny.Percent);
        Assert.True(tiny.Limited);
    }

    [Fact]
    public void AnUnknownWorkAreaDoesNotLimitAnything()
    {
        Assert.Equal(new InterfaceScaleResult(125, 1.25, false), InterfaceScale.Effective(125, 0, 0, 680, 450));
        Assert.Equal(80, InterfaceScale.Effective(10, 0, 0, 680, 450).Percent);
    }

    [Fact]
    public void SettingsDefaultTo100AndOldJsonWithoutTheKeyLoads()
    {
        Assert.Equal(100, new MochiDock.Models.AppSettings().InterfaceScalePercent);
        var old = System.Text.Json.JsonSerializer.Deserialize<MochiDock.Models.AppSettings>("{\"CapsuleTheme\": \"Matcha\"}");
        Assert.Equal(100, old!.InterfaceScalePercent);
        var chosen = System.Text.Json.JsonSerializer.Deserialize<MochiDock.Models.AppSettings>("{\"InterfaceScalePercent\": 150}");
        Assert.Equal(150, chosen!.InterfaceScalePercent);
        Assert.Equal(200, SettingsValidation.ClampInterfaceScale(900));
    }
}
