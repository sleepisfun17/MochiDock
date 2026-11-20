using MochiDock.Core;

namespace MochiDock.Tests;

public sealed class DeviceBatteryProtocolsTests
{
    static byte[] Reply(int length, params (int Index, byte Value)[] bytes)
    {
        var b = new byte[length];
        foreach (var (i, v) in bytes) b[i] = v;
        return b;
    }

    [Fact]
    public void RongyuanRequestIsF7AtIndexOneAndPadded()
    {
        var r = RongyuanProtocol.StatusRequest(65);
        Assert.Equal(65, r.Length);
        Assert.Equal(0, r[0]);
        Assert.Equal(0xF7, r[1]);
        Assert.All(r.Skip(2), b => Assert.Equal(0, b));
    }

    [Fact]
    public void RongyuanParsesBothSlots()
    {
        Assert.True(RongyuanProtocol.TryParse(Reply(65, (2, 82), (3, 64)), out var kb, out var ms));
        Assert.Equal(new RongyuanSlot(true, 82), kb);
        Assert.Equal(new RongyuanSlot(true, 64), ms);
    }

    [Fact]
    public void RongyuanOfflineFlagMeansNotPresent()
    {
        Assert.True(RongyuanProtocol.TryParse(Reply(65, (2, 82), (3, 64), (5, 1)), out var kb, out var ms));
        Assert.True(kb.Present);
        Assert.False(ms.Present);
    }

    [Fact]
    public void RongyuanKeyboardOnly()
    {
        Assert.True(RongyuanProtocol.TryParse(Reply(65, (2, 50), (5, 1)), out var kb, out var ms));
        Assert.Equal(50, kb.Percent);
        Assert.False(ms.Present);
    }

    [Fact]
    public void RongyuanAllZeroOrShortMeansNoAnswer()
    {
        Assert.False(RongyuanProtocol.TryParse(new byte[65], out _, out _));
        Assert.False(RongyuanProtocol.TryParse(new byte[6], out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void RongyuanOutOfRangePercentIsUnknownButPresent(byte value)
    {
        Assert.True(RongyuanProtocol.TryParse(Reply(65, (2, value), (3, 10)), out var kb, out _));
        Assert.True(kb.Present);
        Assert.Null(kb.Percent);
    }

    [Fact]
    public void RongyuanReceiverList()
    {
        Assert.True(RongyuanProtocol.IsReceiver(0x3151, 0x5006));
        Assert.True(RongyuanProtocol.IsReceiver(0x391D, 0x2A05));
        Assert.False(RongyuanProtocol.IsReceiver(0x37B0, 0x0015));
    }

    [Fact]
    public void AjSeriesParse()
    {
        Assert.True(RongyuanProtocol.TryParseAjSeries(Reply(65, (0, 5), (3, 77)), out var kb));
        Assert.Equal(new RongyuanSlot(true, 77), kb);
        Assert.True(RongyuanProtocol.TryParseAjSeries(Reply(65, (0, 5)), out var off));
        Assert.False(off.Present);
        Assert.False(RongyuanProtocol.TryParseAjSeries(Reply(65, (0, 0), (3, 77)), out _));
    }

    [Fact]
    public void LamzuRequestBytes()
    {
        var r = LamzuProtocol.StatusRequest();
        Assert.Equal(65, r.Length);
        Assert.Equal(2, r[3]);
        Assert.Equal(2, r[4]);
        Assert.Equal(0x83, r[6]);
        Assert.Equal(3, r.Count(b => b != 0));
    }

    [Fact]
    public void LamzuParsesBothLayouts()
    {
        Assert.True(LamzuProtocol.TryParse(Reply(65, (1, 0xA1), (4, 2), (6, 0x83), (7, 1), (8, 64)), out var charging, out var pct));
        Assert.True(charging);
        Assert.Equal(64, pct);
        Assert.True(LamzuProtocol.TryParse(Reply(65, (0, 0xA1), (3, 2), (5, 0x83), (6, 0), (7, 31)), out charging, out pct));
        Assert.False(charging);
        Assert.Equal(31, pct);
    }

    [Fact]
    public void LamzuRejectsInvalidReplies()
    {
        Assert.False(LamzuProtocol.TryParse(new byte[65], out _, out _));
        Assert.False(LamzuProtocol.TryParse(Reply(65, (1, 0xA1), (4, 2), (6, 0x83), (8, 101)), out _, out _));
        Assert.False(LamzuProtocol.TryParse(new byte[9], out _, out _));
        Assert.True(LamzuProtocol.IsWaking(Reply(65, (1, 0xA0), (3, 2), (4, 2), (6, 0x83))));
    }

    [Fact]
    public void LamzuConnectionRules()
    {
        Assert.False(LamzuProtocol.IsConnected(0x37B0, 0x0015, 0));
        Assert.True(LamzuProtocol.IsConnected(0x37B0, 0x0015, 40));
        Assert.True(LamzuProtocol.IsCable(0x37B0, 0x0011));
        Assert.True(LamzuProtocol.IsConnected(0x37B0, 0x0011, 0));
        Assert.True(LamzuProtocol.IsCable(0x373E, 0x001C));
        Assert.False(LamzuProtocol.IsCable(0x373E, 0x001D));
    }

    [Fact]
    public void LamzuModelNames()
    {
        Assert.Equal("LAMZU Maya", LamzuProtocol.ModelName(0x37B0, 0x0015));
        Assert.Equal("LAMZU Maya X", LamzuProtocol.ModelName(0x373E, 0x001D));
        Assert.Equal("LAMZU Tachi Lite", LamzuProtocol.ModelName(0x37B0, 0x001E));
        Assert.Equal("LAMZU wireless mouse", LamzuProtocol.ModelName(0x37B0, 0x7777));
    }

    [Fact]
    public void HidScale()
    {
        Assert.Equal(50, HidBatteryScale.Percent(127, 0, 254));
        Assert.Equal(100, HidBatteryScale.Percent(300, 0, 255));
        Assert.Equal(40, HidBatteryScale.Percent(40, 0, 0));
        Assert.Null(HidBatteryScale.Percent(200, 0, 0));
        Assert.Equal(87, HidBatteryScale.Percent(87, 0, 255, 0x05AC)); // Apple quirk
    }

    [Theory]
    [InlineData(0x000540u, BatteryDeviceKind.Keyboard)]
    [InlineData(0x000580u, BatteryDeviceKind.Mouse)]
    [InlineData(0x000504u, BatteryDeviceKind.Gamepad)]
    [InlineData(0x000408u, BatteryDeviceKind.Headset)]
    [InlineData(0x000200u, BatteryDeviceKind.Other)]
    public void BluetoothClassKind(uint cod, BatteryDeviceKind expected) => Assert.Equal(expected, BluetoothClassOfDevice.Kind(cod));
}

public sealed class LogitechProtocolTests
{
    [Fact]
    public void RequestFraming()
    {
        var ping = LogitechProtocol.Ping(true, 1);
        Assert.Equal(20, ping.Length);
        Assert.Equal(new byte[] { 0x11, 0x01, 0x00, 0x1D, 0x00, 0x00, 0x5A }, ping[..7]);
        Assert.Equal(new byte[] { 0x10, 0x02, 0x00, 0x0D, 0x10, 0x00, 0x00 }, LogitechProtocol.FeatureLookup(false, 2, 0x1000));
        Assert.Equal(new byte[] { 0x10, 0x03, 0x81, 0x0D, 0x00, 0x00, 0x00 }, LogitechProtocol.RegisterRead(3, 0x0D));
    }

    [Fact]
    public void AnswerAndErrorMatching()
    {
        var reply = new byte[20]; reply[0] = 0x11; reply[1] = 1; reply[2] = 0x00; reply[3] = 0x1D; reply[4] = 4; reply[5] = 2; reply[6] = 0x5A;
        Assert.True(LogitechProtocol.IsAnswer(reply, 1, 0x00, 0x1D));
        Assert.False(LogitechProtocol.IsAnswer(reply, 2, 0x00, 0x1D)); // another device index
        Assert.Equal(4, LogitechProtocol.ProtocolMajor(LogitechProtocol.Parameters(reply, 1, 0x00, 0x1D)));

        var error = new byte[20]; error[0] = 0x11; error[1] = 1; error[2] = 0xFF; error[3] = 0x00; error[4] = 0x1D; error[5] = 0x09;
        Assert.True(LogitechProtocol.IsAnswer(error, 1, 0x00, 0x1D));
        Assert.True(LogitechProtocol.IsError(error, 1, 0x00, 0x1D));
        Assert.Equal(0x09, LogitechProtocol.ErrorCode(error));
        Assert.Empty(LogitechProtocol.Parameters(error, 1, 0x00, 0x1D));
        Assert.False(LogitechProtocol.IsAnswer(new byte[] { 0x02, 1, 0, 0x1D, 0, 0, 0 }, 1, 0x00, 0x1D)); // not a HID++ report
    }

    [Fact]
    public void ParsesBatteryFeatures()
    {
        Assert.True(LogitechProtocol.TryParseUnified(new byte[] { 80, 0, 0 }, out var p, out var c));
        Assert.Equal(80, p); Assert.False(c);
        Assert.True(LogitechProtocol.TryParseUnified(new byte[] { 0, 4, 1 }, out p, out c));
        Assert.Equal(60, p); Assert.True(c);

        Assert.True(LogitechProtocol.TryParseBatteryStatus(new byte[] { 55, 50, 0 }, out p, out c));
        Assert.Equal(55, p); Assert.False(c);
        Assert.True(LogitechProtocol.TryParseBatteryStatus(new byte[] { 0, 0, 3 }, out p, out c));
        Assert.Equal(100, p); Assert.True(c);
        Assert.False(LogitechProtocol.TryParseBatteryStatus(new byte[] { 55, 50 }, out _, out _));

        Assert.True(LogitechProtocol.TryParseVoltage(new byte[] { 0x10, 0x5A, 0x80 }, out var mv, out p, out c));
        Assert.Equal(4186, mv); Assert.Equal(100, p); Assert.True(c);
    }

    [Theory]
    [InlineData(4300, 100)]
    [InlineData(3922, 70)]
    [InlineData(3700, 16)]
    [InlineData(3500, 0)]
    [InlineData(3000, 0)]
    public void VoltageCurve(int mv, int expected) => Assert.Equal(expected, LogitechProtocol.PercentFromMillivolts(mv));

    [Fact]
    public void LegacyRegisters()
    {
        Assert.True(LogitechProtocol.TryParseRegisterPercent(new byte[] { 72, 0, 0x22 }, out var p, out var c));
        Assert.Equal(72, p); Assert.True(c);
        Assert.False(LogitechProtocol.TryParseRegisterPercent(new byte[] { 0, 0, 0 }, out _, out _));
        Assert.True(LogitechProtocol.TryParseRegisterLevel(new byte[] { 5, 0 }, out p, out c));
        Assert.Equal(50, p); Assert.False(c);
    }

    [Fact]
    public void NamesKindsAndUnitIds()
    {
        Assert.Equal(BatteryDeviceKind.Mouse, LogitechProtocol.KindFromType(3));
        Assert.Equal(BatteryDeviceKind.Keyboard, LogitechProtocol.KindFromType(0));
        Assert.Equal(BatteryDeviceKind.Other, LogitechProtocol.KindFromType(0x77));
        Assert.Equal("G903 LIGHTSPEED Wireless Gaming Mouse", LogitechProtocol.DisplayName("G903 LIGHTSPEED Wireless Gaming Mouse"));
        Assert.Equal("MX Master 3", LogitechProtocol.DisplayName("MX Master 3"));
        Assert.Equal("Logitech Pebble", LogitechProtocol.DisplayName("Pebble"));
        Assert.Equal("Logitech wireless device", LogitechProtocol.DisplayName(""));
        Assert.Equal("12345678", LogitechProtocol.UnitId(new byte[] { 1, 0x12, 0x34, 0x56, 0x78 }));
        Assert.Equal("", LogitechProtocol.UnitId(new byte[] { 1, 0, 0, 0, 0 }));
    }
}

public sealed class DeviceBatteryMergeTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    static DeviceBatteryReading R(string key, string name, BatteryDeviceKind kind, int? pct, DateTimeOffset at, bool connected = true) =>
        new(key, name, kind, "x", pct, false, connected, at);

    [Fact]
    public void MissingDeviceStaysStaleWithinGrace()
    {
        var prev = new[] { R("a", "A", BatteryDeviceKind.Keyboard, 50, T0) };
        var merged = DeviceBatteryMerge.Merge(prev, Array.Empty<DeviceBatteryReading>(), T0.AddMinutes(10));
        var only = Assert.Single(merged);
        Assert.True(only.Stale);
        Assert.Equal(T0, only.ReadAtUtc);
    }

    [Fact]
    public void MissingDeviceDroppedAfterGrace()
    {
        var prev = new[] { R("a", "A", BatteryDeviceKind.Keyboard, 50, T0) };
        Assert.Empty(DeviceBatteryMerge.Merge(prev, Array.Empty<DeviceBatteryReading>(), T0.AddMinutes(31)));
    }

    [Fact]
    public void StaleDoesNotExtendGrace()
    {
        var first = DeviceBatteryMerge.Merge(new[] { R("a", "A", BatteryDeviceKind.Keyboard, 50, T0) }, Array.Empty<DeviceBatteryReading>(), T0.AddMinutes(20));
        Assert.Empty(DeviceBatteryMerge.Merge(first, Array.Empty<DeviceBatteryReading>(), T0.AddMinutes(35)));
    }

    [Fact]
    public void FreshReadingReplacesStaleAndDisconnectedIsDropped()
    {
        var prev = new[] { R("a", "A", BatteryDeviceKind.Keyboard, 50, T0) with { Stale = true } };
        var polled = new[] { R("a", "A", BatteryDeviceKind.Keyboard, 48, T0.AddMinutes(1)), R("b", "B", BatteryDeviceKind.Mouse, 0, T0.AddMinutes(1), false) };
        var merged = DeviceBatteryMerge.Merge(prev, polled, T0.AddMinutes(1));
        var only = Assert.Single(merged);
        Assert.False(only.Stale);
        Assert.Equal(48, only.Percent);
    }

    [Fact]
    public void OrderedByKindThenName()
    {
        var polled = new[]
        {
            R("1", "Zed", BatteryDeviceKind.Other, 1, T0), R("2", "Pad", BatteryDeviceKind.Gamepad, 1, T0), R("3", "Buds", BatteryDeviceKind.Headset, 1, T0),
            R("4", "Mx", BatteryDeviceKind.Mouse, 1, T0), R("5", "B-key", BatteryDeviceKind.Keyboard, 1, T0), R("6", "A-key", BatteryDeviceKind.Keyboard, 1, T0),
        };
        Assert.Equal(new[] { "A-key", "B-key", "Mx", "Buds", "Pad", "Zed" }, DeviceBatteryMerge.Merge(Array.Empty<DeviceBatteryReading>(), polled, T0).Select(r => r.Name));
    }
}

public sealed class DeviceBatteryAlertPolicyTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    static IReadOnlyList<DeviceBatteryReading> L(int? pct, bool charging = false, bool stale = false, string key = "k") =>
        new[] { new DeviceBatteryReading(key, "Keys", BatteryDeviceKind.Keyboard, "x", pct, charging, true, Now, stale) };

    [Fact]
    public void FiresAtThresholdOnceAndFirstSightingCounts()
    {
        var p = new DeviceBatteryAlertPolicy();
        Assert.Null(p.Evaluate(L(16), Now));
        var fired = p.Evaluate(L(15), Now);
        Assert.NotNull(fired);
        Assert.Equal("Keyboard battery 15%", DeviceBatteryAlertPolicy.Text(fired!));
        Assert.Null(p.Evaluate(L(14), Now));
        Assert.NotNull(new DeviceBatteryAlertPolicy().Evaluate(L(9), Now));
    }

    [Fact]
    public void RearmsAboveTwentyPercent()
    {
        var p = new DeviceBatteryAlertPolicy();
        Assert.NotNull(p.Evaluate(L(10), Now));
        Assert.Null(p.Evaluate(L(18), Now));
        Assert.Null(p.Evaluate(L(12), Now));
        Assert.Null(p.Evaluate(L(21), Now));
        Assert.NotNull(p.Evaluate(L(12), Now));
    }

    [Fact]
    public void ChargingSuppressesAndRearms()
    {
        var p = new DeviceBatteryAlertPolicy();
        Assert.Null(p.Evaluate(L(10, charging: true), Now));
        Assert.NotNull(p.Evaluate(L(10), Now));
        Assert.Null(p.Evaluate(L(10), Now));
        Assert.Null(p.Evaluate(L(11, charging: true), Now));
        Assert.NotNull(p.Evaluate(L(11), Now));
    }

    [Fact]
    public void StaleAndUnknownAreIgnored()
    {
        var p = new DeviceBatteryAlertPolicy();
        Assert.Null(p.Evaluate(L(5, stale: true), Now));
        Assert.Null(p.Evaluate(L(null), Now));
        Assert.NotNull(p.Evaluate(L(5), Now));
    }
}

public sealed class DeviceBatterySettingsTests
{
    [Fact]
    public void DeviceBatteriesAreOffByDefaultAndTaskbarIsOptOut()
    {
        var settings = new MochiDock.Models.AppSettings();
        Assert.False(settings.EnableDeviceBatteries);
        Assert.True(settings.ShowDeviceBatteriesOnTaskbar);
    }

    [Fact]
    public void OldSettingsFileWithTheRemovedBatteryModuleKeyStillLoads()
    {
        // The Battery page is always available now; a saved "ShowBatteryModule" is an unknown key and must not break loading.
        var settings = System.Text.Json.JsonSerializer.Deserialize<MochiDock.Models.AppSettings>("{\"ShowBatteryModule\": false, \"EnableDeviceBatteries\": true}");
        Assert.NotNull(settings);
        Assert.True(settings!.EnableDeviceBatteries);
    }

    [Fact]
    public void ChipShowsSystemBatteryOnLaptop()
    {
        var info = MochiDock.Core.BatteryChipPolicy.Summary(true, 64, true, System.Array.Empty<MochiDock.Core.DeviceBatteryReading>());
        Assert.Equal(("Battery", "64%", 64, true), (info.Label, info.Text, info.Percent, info.Charging));
    }

    [Fact]
    public void ChipShowsPsuOnDesktopWithoutDevices()
    {
        var info = MochiDock.Core.BatteryChipPolicy.Summary(false, 0, false, System.Array.Empty<MochiDock.Core.DeviceBatteryReading>());
        Assert.Equal(("Power", "PSU", (int?)null), (info.Label, info.Text, info.Percent));
    }

    [Fact]
    public void ChipShowsLowestDeviceOnDesktop()
    {
        var now = System.DateTimeOffset.UtcNow;
        var devices = new[]
        {
            new MochiDock.Core.DeviceBatteryReading("a", "Mouse", MochiDock.Core.BatteryDeviceKind.Mouse, "x", 64, false, true, now),
            new MochiDock.Core.DeviceBatteryReading("b", "Buds", MochiDock.Core.BatteryDeviceKind.Headset, "x", 22, false, true, now),
        };
        var info = MochiDock.Core.BatteryChipPolicy.Summary(false, 0, false, devices);
        Assert.Equal(("Battery", "22%", MochiDock.Core.BatteryDeviceKind.Headset), (info.Label, info.Text, info.Kind));
    }

    [Fact]
    public void MergeDropsADeviceThatPollsAsDisconnected()
    {
        var now = System.DateTimeOffset.UtcNow;
        var before = new[] { new MochiDock.Core.DeviceBatteryReading("bt:x", "Buds", MochiDock.Core.BatteryDeviceKind.Headset, "Bluetooth", 90, false, true, now) };
        var polled = new[] { before[0] with { Connected = false } };
        Assert.Empty(MochiDock.Core.DeviceBatteryMerge.Merge(before, polled, now));
    }

    const string PhoneCard = "{\"type\":\"AdaptiveCard\",\"speak\":\"Pixel 8 - Connected to your PC\",\"body\":[{\"type\":\"Image\",\"tooltip\":\"Battery 87% remaining\"}]}";

    [Fact]
    public void PhoneLinkParsesNameAndBattery()
    {
        var now = System.DateTimeOffset.UtcNow;
        Assert.True(MochiDock.Core.PhoneLinkProtocol.TryParse(PhoneCard, now.AddMinutes(-5), now, out var name, out var percent, out var stale));
        Assert.Equal(("Pixel 8", 87, false), (name, percent, stale));
    }

    [Fact]
    public void PhoneLinkMarksAnOldCardStale()
    {
        var now = System.DateTimeOffset.UtcNow;
        Assert.True(MochiDock.Core.PhoneLinkProtocol.TryParse(PhoneCard, now.AddHours(-4), now, out _, out _, out var stale));
        Assert.True(stale);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"speak\":\"Pixel 8 - Connected to your PC\"}")]                       // no battery text
    [InlineData("{\"speak\":\"Pixel 8\",\"t\":\"Battery 50% remaining\"}")]               // not the connected card
    [InlineData("{\"speak\":\"P - Connected to your PC\",\"t\":\"Battery 250% remaining\"}")]
    public void PhoneLinkRejectsAnythingUnexpected(string json)
    {
        var now = System.DateTimeOffset.UtcNow;
        Assert.False(MochiDock.Core.PhoneLinkProtocol.TryParse(json, now, now, out _, out _, out _));
    }

    [Fact]
    public void DedupeKeepsTheCableReadingOverTheReceiverOne()
    {
        var now = System.DateTimeOffset.UtcNow;
        var receiver = new MochiDock.Core.DeviceBatteryReading("lamzu:1", "LAMZU Maya", MochiDock.Core.BatteryDeviceKind.Mouse, "2.4 GHz receiver", 70, false, true, now);
        var cable = new MochiDock.Core.DeviceBatteryReading("lamzu:2", "LAMZU Maya", MochiDock.Core.BatteryDeviceKind.Mouse, "USB cable", 71, true, true, now);
        var other = new MochiDock.Core.DeviceBatteryReading("bt:1", "Buds", MochiDock.Core.BatteryDeviceKind.Headset, "Bluetooth", 90, false, true, now);
        var result = MochiDock.Core.DeviceBatteryDedupe.Collapse(new[] { receiver, cable, other });
        Assert.Equal(2, result.Count);
        Assert.Contains(cable, result);
        Assert.Contains(other, result);
    }

    [Fact]
    public void DedupePrefersAConnectedTwinOverADisconnectedOne()
    {
        var now = System.DateTimeOffset.UtcNow;
        var a = new MochiDock.Core.DeviceBatteryReading("bt:a", "Galaxy100 Lite-1", MochiDock.Core.BatteryDeviceKind.Other, "Bluetooth", 78, false, false, now);
        var b = new MochiDock.Core.DeviceBatteryReading("bt:b", "Galaxy100 Lite-1", MochiDock.Core.BatteryDeviceKind.Other, "Bluetooth", 100, false, true, now);
        Assert.Equal(new[] { b }, MochiDock.Core.DeviceBatteryDedupe.Collapse(new[] { a, b }));
    }

    [Fact]
    public void HiddenDevicesAreFilteredByKey()
    {
        var now = System.DateTimeOffset.UtcNow;
        var one = new MochiDock.Core.DeviceBatteryReading("k1", "A", MochiDock.Core.BatteryDeviceKind.Mouse, "x", 50, false, true, now);
        var two = new MochiDock.Core.DeviceBatteryReading("k2", "B", MochiDock.Core.BatteryDeviceKind.Mouse, "x", 60, false, true, now);
        Assert.Equal(new[] { two }, MochiDock.Core.DeviceBatteryVisibility.Visible(new[] { one, two }, new[] { "K1" }));
        Assert.Equal(2, MochiDock.Core.DeviceBatteryVisibility.Visible(new[] { one, two }, null).Count);
    }

    [Fact]
    public void WidgetSettingsDefaultOff()
    {
        var settings = new MochiDock.Models.AppSettings();
        Assert.False(settings.ShowDeviceBatteryBackground);
        Assert.Empty(settings.HiddenDeviceBatteryKeys);
    }

    [Fact]
    public void CompxRequestsCarryTheChecksum()
    {
        var request = MochiDock.Core.CompxProtocol.Request(MochiDock.Core.CompxProtocol.BatteryCommand);
        Assert.Equal(17, request.Length);
        Assert.Equal((byte)0x08, request[0]);
        Assert.Equal((byte)0x04, request[1]);
        Assert.Equal((byte)(0x55 - 0x08 - 0x04), request[16]);
        Assert.True(MochiDock.Core.CompxProtocol.IsReply(request, MochiDock.Core.CompxProtocol.BatteryCommand)); // same frame shape and checksum
    }

    [Fact]
    public void CompxParsesBatteryAndRejectsBadReplies()
    {
        var reply = new byte[17];
        reply[0] = 0x08; reply[1] = 0x04; reply[6] = 73; reply[7] = 1; reply[8] = 0x0F; reply[9] = 0xA0;
        reply[16] = MochiDock.Core.CompxProtocol.Checksum(reply);
        Assert.True(MochiDock.Core.CompxProtocol.TryParseBattery(reply, out var percent, out var charging, out var mv));
        Assert.Equal((73, true, 4000), (percent, charging, mv));

        reply[16]++;                                                        // wrong checksum
        Assert.False(MochiDock.Core.CompxProtocol.TryParseBattery(reply, out _, out _, out _));
        reply[16]--; reply[6] = 150;                                        // out of range
        reply[16] = MochiDock.Core.CompxProtocol.Checksum(reply);
        Assert.False(MochiDock.Core.CompxProtocol.TryParseBattery(reply, out _, out _, out _));
    }

    [Fact]
    public void CompxOnlineStatusIsByteSix()
    {
        var reply = MochiDock.Core.CompxProtocol.Request(MochiDock.Core.CompxProtocol.OnlineCommand);
        Assert.False(MochiDock.Core.CompxProtocol.IsOnline(reply));
        reply[6] = 1;
        Assert.True(MochiDock.Core.CompxProtocol.IsOnline(reply));
    }

    [Fact]
    public void SonixRequestAndReplyUseTheByteSumChecksum()
    {
        var request = MochiDock.Core.SonixProtocol.StatusRequest();
        Assert.Equal(33, request.Length);
        Assert.Equal((byte)0x20, request[1]);
        Assert.Equal((byte)0x01, request[2]);
        Assert.Equal((byte)0x21, request[32]);

        var reply = new byte[33];
        reply[1] = 0x20; reply[2] = 0x01; reply[4] = 64;
        reply[32] = (byte)(0x20 + 0x01 + 64);
        Assert.Equal(64, MochiDock.Core.SonixProtocol.Level(reply));

        reply[32]++;
        Assert.Null(MochiDock.Core.SonixProtocol.Level(reply));          // bad checksum
        reply[32]--; reply[4] = 0; reply[32] = (byte)(0x20 + 0x01);
        Assert.Null(MochiDock.Core.SonixProtocol.Level(reply));          // level 0: keyboard off or in another mode
    }

    [Theory]
    [InlineData("Epomaker Galaxy100 Lite-2.4G", "Epomaker Galaxy100 Lite")]
    [InlineData("AJAZZ AK820 MAX 2.4G", "AJAZZ AK820 MAX")]
    [InlineData("", "Wireless keyboard")]
    public void SonixKeyboardNameDropsTheDongleSuffix(string product, string expected) =>
        Assert.Equal(expected, MochiDock.Core.SonixProtocol.KeyboardName(product));
}

public class DeviceBatteryTableTests
{
    [Fact]
    public void LamzuProductsAreUniqueAndOnLamzusVendorIds()
    {
        var products = MochiDock.Core.LamzuProtocol.KnownProducts;
        Assert.Equal(products.Count, products.Select(p => (p.Vid, p.Pid)).Distinct().Count());
        Assert.All(products, p => Assert.True(MochiDock.Core.LamzuProtocol.IsVendor(p.Vid)));
        Assert.All(products, p => Assert.StartsWith("LAMZU ", p.Name));
    }

    [Theory]
    [InlineData(0x37B0, 0x0042, "LAMZU Orcus", true)]
    [InlineData(0x37B0, 0x0056, "LAMZU Orcus V2", true)]   // the vendor list names 0x0056 Orcus V2
    [InlineData(0x37B0, 0x0034, "LAMZU DM198", true)]
    [InlineData(0x37B0, 0x005A, "LAMZU Inca V2", true)]
    [InlineData(0x37B0, 0x0054, "LAMZU Mini", false)]
    [InlineData(0x37B0, 0x0048, "LAMZU wireless mouse", false)] // 8K receiver shared by six models
    [InlineData(0x37B0, 0x0036, "LAMZU wireless mouse", false)]
    [InlineData(0x373E, 0x0016, "LAMZU Maya X V2", false)]
    public void LamzuProductsFollowTheVendorList(int vid, int pid, string name, bool cable)
    {
        Assert.Equal(name, MochiDock.Core.LamzuProtocol.ModelName((ushort)vid, (ushort)pid));
        Assert.Equal(cable, MochiDock.Core.LamzuProtocol.IsCable((ushort)vid, (ushort)pid));
    }

    [Fact]
    public void RongyuanReceiverListIsComplete()
    {
        // 81 receivers across 13 vendor IDs, as in BatteryTray's list from the ATTACK SHARK web driver.
        int[] samples = { 0x0DB0197E, 0x14A5C045, 0x3151402D, 0x342DE3FD, 0x374AA355, 0x38EE0000, 0x391D2A05 };
        Assert.All(samples, k => Assert.True(MochiDock.Core.RongyuanProtocol.IsReceiver((ushort)(k >> 16), (ushort)(k & 0xFFFF))));
        Assert.False(MochiDock.Core.RongyuanProtocol.IsReceiver(0x3151, 0x400B)); // a cable mouse, not a receiver
    }
}
