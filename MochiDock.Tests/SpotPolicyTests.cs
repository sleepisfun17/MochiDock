using Xunit;
using MochiDock.Core;

namespace MochiDock.Tests;

public class SpotPolicyTests
{
    [Fact]
    public void CenteredByDefault_WhenRatioIsNull()
    {
        // 1920 screen, 620 window width -> (1920 - 620) / 2 = 650
        double left = SpotPolicy.ComputeWindowLeftDip(0, 1920, 620, null);
        Assert.Equal(650, left);
    }

    [Fact]
    public void DragRatio_ClampedWithinMonitorBounds()
    {
        // Drag to left edge (ratio 0.0) -> clamped to screenLeft (0)
        double leftMin = SpotPolicy.ComputeWindowLeftDip(0, 1920, 620, 0.0);
        Assert.Equal(0, leftMin);

        // Drag to right edge (ratio 1.0) -> clamped to 1920 - 620 = 1300
        double leftMax = SpotPolicy.ComputeWindowLeftDip(0, 1920, 620, 1.0);
        Assert.Equal(1300, leftMax);
    }

    [Fact]
    public void RememberRatio_SnapsToNullNearCenter()
    {
        double screenW = 1920;
        double center = 960;
        // Within 16 DIPs -> null
        Assert.Null(SpotPolicy.RememberRatio(center, screenW));
        Assert.Null(SpotPolicy.RememberRatio(center + 10, screenW));
        Assert.Null(SpotPolicy.RememberRatio(center - 15, screenW));

        // Outside 16 DIPs -> returns ratio
        double? ratio = SpotPolicy.RememberRatio(1440, screenW);
        Assert.NotNull(ratio);
        Assert.Equal(0.75, ratio.Value, 2);
    }
}

