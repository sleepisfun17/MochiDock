using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class FxRatesPolicyTests
{
    [Fact]
    public void Calculate24hDeltaFindsClosestBarAndComputesCorrectPercent()
    {
        var now = DateTimeOffset.UtcNow;
        var bars = new (DateTimeOffset At, decimal Value)[]
        {
            (now.AddHours(-48), 100m),
            (now.AddHours(-24.1), 110m), // ~24h ago
            (now.AddHours(-12), 115m),
            (now.AddHours(-1), 120m)
        };

        var current = 121m;
        var delta = FxMath.Calculate24hDelta(current, bars, now);

        Assert.NotNull(delta);
        // (121 - 110) / 110 * 100 = 10%
        Assert.Equal(10.0m, Math.Round(delta!.Value, 1));
    }

    [Fact]
    public void Calculate24hDeltaReturnsNullOnEmptyOrInvalid()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(FxMath.Calculate24hDelta(0m, [], now));
        Assert.Null(FxMath.Calculate24hDelta(-5m, [(now.AddHours(-24), 100m)], now));
        Assert.Null(FxMath.Calculate24hDelta(100m, [], now));
    }
}

