using MochiDock.Core;
using MochiDock.Services;
using Xunit.Abstractions;

public class EnergyMeterTests
{
    private readonly ITestOutputHelper _output;
    public EnergyMeterTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(18032.0, 18.032)]
    [InlineData(1000.0, 1.0)]
    [InlineData(999000.0, 999.0)]
    public void ConvertsMilliwattsToWatts(double milliwatts, double watts) =>
        Assert.Equal(watts, EnergyMeterPower.ToWatts(milliwatts)!.Value, 3);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(2_000_000.0)]
    public void RejectsZeroNegativeAndImplausibleValues(double milliwatts) =>
        Assert.Null(EnergyMeterPower.ToWatts(milliwatts));

    [Fact]
    public async Task ReaderNeverThrowsAndOnlyReturnsPlausibleWatts()
    {
        using var reader = new EnergyMeterReader();
        reader.Sample();
        await Task.Delay(1200);
        var (package, dram) = reader.Sample();
        _output.WriteLine($"package={package?.ToString("0.0") ?? "n/a"} W, dram={dram?.ToString("0.0") ?? "n/a"} W");
        if (package is { } p) Assert.InRange(p, 0.001, 1000);
        if (dram is { } d) Assert.InRange(d, 0.001, 1000);
    }

    [Fact]
    public void ASampleAfterDisposeIsEmpty()
    {
        var reader = new EnergyMeterReader();
        reader.Dispose();
        Assert.Equal((null, null), reader.Sample());
    }
}
