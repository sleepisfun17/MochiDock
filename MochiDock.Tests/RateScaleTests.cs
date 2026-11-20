using System.Globalization;
using MochiDock.Core;

public class RateScaleTests
{
    const double K = 1024, M = 1024 * 1024;

    [Theory]
    [InlineData(0, "0 KB/s")]
    [InlineData(100, "0 KB/s")]
    [InlineData(12.5 * K, "12.5 KB/s")]
    [InlineData(350 * K, "350 KB/s")]
    [InlineData(1 * M, "1.0 MB/s")]
    [InlineData(5 * M, "5.0 MB/s")]          // was "51 MB/s": the old "0.1" format printed a literal 1 instead of a decimal
    [InlineData(2.44 * M, "2.4 MB/s")]
    [InlineData(125 * M, "125 MB/s")]
    [InlineData(double.NaN, "0 KB/s")]
    public void FormatsRates(double bytes, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(expected, RateScale.Format(bytes));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(0, 1 * K)]
    [InlineData(500, 1 * K)]
    [InlineData(1.2 * K, 1.5 * K)]
    [InlineData(2 * K, 2 * K)]
    [InlineData(2.1 * K, 3 * K)]
    [InlineData(6 * K, 7.5 * K)]
    [InlineData(80 * K, 100 * K)]
    [InlineData(420 * K, 500 * K)]
    [InlineData(990 * K, 1000 * K)]
    [InlineData(1010 * K, 1 * M)]
    [InlineData(1.3 * M, 1.5 * M)]
    [InlineData(37 * M, 50 * M)]
    [InlineData(800 * M, 1000 * M)]
    public void CeilingIsTheNextRoundValue(double peak, double expected) =>
        Assert.Equal(expected, RateScale.NiceCeiling(peak), 6);

    [Fact]
    public void CeilingNeverSitsBelowThePeak()
    {
        for (double peak = 1; peak < 2e9; peak *= 1.37)
            Assert.True(RateScale.NiceCeiling(peak) >= peak);
    }
}
