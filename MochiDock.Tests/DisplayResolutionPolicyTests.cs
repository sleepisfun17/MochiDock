using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class DisplayResolutionPolicyTests
{
    [Fact]
    public void AspectRatioMatchesKnownStandards()
    {
        Assert.Equal("16:9", DisplayResolutionPolicy.GetAspectRatio(2560, 1440));
        Assert.Equal("16:9", DisplayResolutionPolicy.GetAspectRatio(1920, 1080));
        Assert.Equal("16:10", DisplayResolutionPolicy.GetAspectRatio(2304, 1440));
        Assert.Equal("16:10", DisplayResolutionPolicy.GetAspectRatio(1920, 1200));
        Assert.Equal("4:3", DisplayResolutionPolicy.GetAspectRatio(1440, 1080));
        Assert.Equal("4:3", DisplayResolutionPolicy.GetAspectRatio(1280, 960));
        Assert.Equal("4:3", DisplayResolutionPolicy.GetAspectRatio(1024, 768));
        Assert.Equal("5:4", DisplayResolutionPolicy.GetAspectRatio(1280, 1024));
    }

    [Fact]
    public void FormatLabelIncludesNativeAndAspectRatio()
    {
        Assert.Equal("2560×1440 (16:9 Native)", DisplayResolutionPolicy.FormatLabel(2560, 1440, isNative: true));
        Assert.Equal("2304×1440 (16:10)", DisplayResolutionPolicy.FormatLabel(2304, 1440, isNative: false));
    }

    [Fact]
    public void ParseResolutionsHandlesVariousDelimiters()
    {
        Assert.True(DisplayResolutionPolicy.TryParse("2304x1440", out int w1, out int h1));
        Assert.Equal(2304, w1);
        Assert.Equal(1440, h1);

        Assert.True(DisplayResolutionPolicy.TryParse("2560×1440", out int w2, out int h2));
        Assert.Equal(2560, w2);
        Assert.Equal(1440, h2);

        Assert.True(DisplayResolutionPolicy.TryParse(" 1920 : 1080 ", out int w3, out int h3));
        Assert.Equal(1920, w3);
        Assert.Equal(1080, h3);

        Assert.False(DisplayResolutionPolicy.TryParse("invalid", out _, out _));
        Assert.False(DisplayResolutionPolicy.TryParse(null, out _, out _));
    }

    [Fact]
    public void ToggleAlternatesBetweenNativeAndPreferredGamingResolution()
    {
        // When on Native (2560x1440), toggle to Gaming (2304x1440)
        var toGaming = DisplayResolutionPolicy.GetToggleResolution(2560, 1440, 2560, 1440, "2304x1440");
        Assert.Equal((2304, 1440), toGaming);

        // When on Gaming (2304x1440), toggle back to Native (2560x1440)
        var toNative = DisplayResolutionPolicy.GetToggleResolution(2304, 1440, 2560, 1440, "2304x1440");
        Assert.Equal((2560, 1440), toNative);

        // When on an arbitrary resolution (1920x1080), toggle to Gaming (2304x1440)
        var fromOther = DisplayResolutionPolicy.GetToggleResolution(1920, 1080, 2560, 1440, "2304x1440");
        Assert.Equal((2304, 1440), fromOther);
    }

    [Fact]
    public void NativeIsTheLargestSupportedModeNotTheCurrentOne()
    {
        var modes = new[] { (1920, 1080), (2304, 1440), (2560, 1440), (1280, 720), (2560, 1440) };
        Assert.Equal((2560, 1440), DisplayResolutionPolicy.PickNative(modes, 1024, 768));
        Assert.Equal((2560, 1600), DisplayResolutionPolicy.PickNative(modes.Append((2560, 1600)), 1024, 768));
        Assert.Equal((1024, 768), DisplayResolutionPolicy.PickNative(System.Array.Empty<(int, int)>(), 1024, 768));   // no modes: the fallback
        Assert.Equal((2560, 1440), DisplayResolutionPolicy.PickNative(new[] { (0, 0), (-5, 10) }, 0, 0));              // junk modes and no fallback
    }

    [Fact]
    public void TheToggleGoesBackToTheResolutionBeforeNotTheScreenSizeNow()
    {
        // The bug: "native" used to be the screen's current size. After the first toggle that is the gaming size, so going back did nothing.
        var native = DisplayResolutionPolicy.PickNative(new[] { (1920, 1080), (2304, 1440), (2560, 1440) }, 1024, 768);
        Assert.Equal((2304, 1440), DisplayResolutionPolicy.GetToggleResolution(2560, 1440, native.Width, native.Height, "2304x1440"));   // to gaming
        Assert.Equal((2560, 1440), DisplayResolutionPolicy.GetToggleResolution(2304, 1440, native.Width, native.Height, "2304x1440"));   // and back
        // A remembered resolution wins as the way back.
        Assert.Equal((1920, 1080), DisplayResolutionPolicy.GetToggleResolution(2304, 1440, 1920, 1080, "2304x1440"));
    }
}
