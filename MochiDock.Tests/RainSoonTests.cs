using MochiDock.Core;

public class RainSoonTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    static List<PrecipitationSlot> Slots(DateTimeOffset start, params (double mm, int code)[] steps) =>
        steps.Select((s, i) => new PrecipitationSlot(start.AddMinutes(15 * i), s.mm, s.code)).ToList();

    [Theory]
    [InlineData(95, 0, RainSoonKind.Thunderstorm)]
    [InlineData(73, 0, RainSoonKind.Snow)]
    [InlineData(86, 0, RainSoonKind.Snow)]
    [InlineData(61, 0, RainSoonKind.Rain)]
    [InlineData(81, 0, RainSoonKind.Rain)]
    [InlineData(3, 0.4, RainSoonKind.Rain)]
    [InlineData(3, 0.1, RainSoonKind.None)]
    [InlineData(0, 0, RainSoonKind.None)]
    public void KindFollowsTheWeatherCodeOrTheAmount(int code, double mm, RainSoonKind expected) =>
        Assert.Equal(expected, RainSoonPolicy.KindOf(code, mm));

    [Fact]
    public void WarnsAboutTheFirstWetSlotWithinTheHour()
    {
        var slots = Slots(T0, (0, 0), (0, 3), (0.5, 61), (1, 63));
        var notice = RainSoonPolicy.Upcoming(T0.AddMinutes(10), slots);
        Assert.Equal(new RainSoonNotice(RainSoonKind.Rain, 20), notice);
        Assert.Equal("Rain in about 20 min", notice!.Value.Text);
        Assert.Equal("Snow starting soon", new RainSoonNotice(RainSoonKind.Snow, 5).Text);
    }

    [Fact]
    public void NothingWhenItIsAlreadyRainingOrRainIsFarOff()
    {
        Assert.Null(RainSoonPolicy.Upcoming(T0.AddMinutes(1), Slots(T0, (0.5, 61), (0, 0), (0.5, 61))));
        Assert.Null(RainSoonPolicy.Upcoming(T0, Slots(T0, (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (1, 61))));   // 75 min away
        Assert.Null(RainSoonPolicy.Upcoming(T0, new List<PrecipitationSlot>()));
    }

    [Fact]
    public void FiresOnceAndRearmsAfterAnHourDry()
    {
        var policy = new RainSoonPolicy();
        var wetSoon = Slots(T0, (0, 0), (0, 0), (1, 61));
        Assert.NotNull(policy.Evaluate(T0, wetSoon));
        Assert.Null(policy.Evaluate(T0.AddMinutes(10), wetSoon));                       // same shower: once
        var dry = Slots(T0.AddMinutes(30), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0));
        Assert.Null(policy.Evaluate(T0.AddMinutes(40), dry));
        var later = Slots(T0.AddMinutes(75), (0, 0), (1, 61));
        Assert.Null(policy.Evaluate(T0.AddMinutes(75), later));                         // never dry for a whole hour yet: still quiet
        Assert.Null(policy.Evaluate(T0.AddMinutes(150), Slots(T0.AddMinutes(150), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0))));   // dry for over an hour: re-armed
        Assert.NotNull(policy.Evaluate(T0.AddMinutes(160), Slots(T0.AddMinutes(150), (0, 0), (0, 0), (1, 95))));
    }

    [Fact]
    public void RainingNowDisarmsSoABriefPauseDoesNotWarn()
    {
        var policy = new RainSoonPolicy();
        Assert.Null(policy.Evaluate(T0, Slots(T0, (1, 61), (0, 0), (1, 61))));
        Assert.Null(policy.Evaluate(T0.AddMinutes(20), Slots(T0.AddMinutes(15), (0, 0), (1, 61))));
    }

    [Fact]
    public void ParsesOpenMeteoTimesInTheirZone()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 6, 30, 0, TimeSpan.Zero), RainSoonPolicy.ParseTime("2026-11-20T15:30", 9 * 3600)!.Value.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 15, 30, 0, TimeSpan.Zero), RainSoonPolicy.ParseTime("2026-11-20T15:30", 0));
        Assert.Null(RainSoonPolicy.ParseTime("soon", 0));
    }
}
