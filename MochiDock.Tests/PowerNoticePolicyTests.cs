using MochiDock.Core;

public class PowerNoticePolicyTests
{
    static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-11T10:00:00Z");
    static PowerState Battery(int percent) => new(true, false, percent);
    static PowerState Ac(int percent) => new(true, true, percent);

    [Fact]
    public void FirstReadingNeverNotifies()
    {
        var p = new PowerNoticePolicy();
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(15), T0));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(14), T0.AddMinutes(1)));   // joined below 20: already "used up"
    }

    [Fact]
    public void PlugAndUnplugNotifyOnce()
    {
        var p = new PowerNoticePolicy();
        p.Evaluate(Battery(60), T0);
        Assert.Equal(PowerNoticeKind.PluggedIn, p.Evaluate(Ac(60), T0.AddSeconds(10)));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Ac(61), T0.AddSeconds(20)));
        Assert.Equal(PowerNoticeKind.Unplugged, p.Evaluate(Battery(61), T0.AddSeconds(30)));
    }

    [Fact]
    public void RapidFlappingIsIgnored()
    {
        var p = new PowerNoticePolicy();
        p.Evaluate(Battery(60), T0);
        Assert.Equal(PowerNoticeKind.PluggedIn, p.Evaluate(Ac(60), T0.AddSeconds(10)));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(60), T0.AddSeconds(11)));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Ac(60), T0.AddSeconds(11.5)));
    }

    [Fact]
    public void LowThenCriticalEachFireOncePerDescent()
    {
        var p = new PowerNoticePolicy();
        p.Evaluate(Battery(30), T0);
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(21), T0.AddMinutes(1)));
        Assert.Equal(PowerNoticeKind.LowBattery, p.Evaluate(Battery(20), T0.AddMinutes(2)));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(19), T0.AddMinutes(3)));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(11), T0.AddMinutes(4)));
        Assert.Equal(PowerNoticeKind.CriticalBattery, p.Evaluate(Battery(10), T0.AddMinutes(5)));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(9), T0.AddMinutes(6)));
    }

    [Fact]
    public void JumpingStraightToCriticalSkipsTheLowNotice()
    {
        var p = new PowerNoticePolicy();
        p.Evaluate(Battery(30), T0);
        Assert.Equal(PowerNoticeKind.CriticalBattery, p.Evaluate(Battery(9), T0.AddMinutes(1)));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(8), T0.AddMinutes(2)));
    }

    [Fact]
    public void ChargingReArmsTheWarnings()
    {
        var p = new PowerNoticePolicy();
        p.Evaluate(Battery(30), T0);
        Assert.Equal(PowerNoticeKind.LowBattery, p.Evaluate(Battery(19), T0.AddMinutes(1)));
        Assert.Equal(PowerNoticeKind.PluggedIn, p.Evaluate(Ac(19), T0.AddMinutes(2)));
        // Unplugging while still low gives the warning (re-armed by charging) rather than a bare "On battery", and only once.
        Assert.Equal(PowerNoticeKind.LowBattery, p.Evaluate(Battery(19), T0.AddMinutes(3)));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(18), T0.AddMinutes(4)));
    }

    [Fact]
    public void UnplugAboveTheThresholdStillAnnouncesOnBattery()
    {
        var p = new PowerNoticePolicy();
        p.Evaluate(Ac(80), T0);
        Assert.Equal(PowerNoticeKind.Unplugged, p.Evaluate(Battery(80), T0.AddMinutes(1)));
        Assert.Equal(PowerNoticeKind.LowBattery, p.Evaluate(Battery(20), T0.AddMinutes(30)));
    }

    [Fact]
    public void RecoveryAboveTheMarginReArmsWithoutACharger()
    {
        var p = new PowerNoticePolicy();
        p.Evaluate(Battery(30), T0);
        Assert.Equal(PowerNoticeKind.LowBattery, p.Evaluate(Battery(20), T0.AddMinutes(1)));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(23), T0.AddMinutes(2)));   // still within the hysteresis band
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(20), T0.AddMinutes(3)));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(26), T0.AddMinutes(4)));
        Assert.Equal(PowerNoticeKind.LowBattery, p.Evaluate(Battery(20), T0.AddMinutes(5)));
    }

    [Fact]
    public void NoBatteryOrUnknownLevelNeverNotifies()
    {
        var p = new PowerNoticePolicy();
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(new PowerState(false, true, 0), T0));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(new PowerState(false, false, 255), T0.AddMinutes(1)));
        Assert.Equal(PowerNoticeKind.None, p.Evaluate(Battery(120), T0.AddMinutes(2)));
    }

    [Fact]
    public void TextAndDurationsMatchTheKind()
    {
        Assert.Equal("Charging · 54%", PowerNoticePolicy.Text(PowerNoticeKind.PluggedIn, 54));
        Assert.Equal("Battery critical · 9%", PowerNoticePolicy.Text(PowerNoticeKind.CriticalBattery, 9));
        Assert.Equal(2.5, PowerNoticePolicy.DisplaySeconds(PowerNoticeKind.Unplugged));
        Assert.Equal(6, PowerNoticePolicy.DisplaySeconds(PowerNoticeKind.LowBattery));
    }
}
