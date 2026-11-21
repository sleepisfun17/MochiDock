namespace MochiDock.Core;

public enum BatteryDeviceKind { Other, Mouse, Keyboard, Headset, Gamepad, Phone }

/// <summary>One wireless device battery reading. <paramref name="Percent"/> is null when the device is present but the level is unknown.</summary>
public sealed record DeviceBatteryReading(string Key, string Name, BatteryDeviceKind Kind, string Via, int? Percent, bool Charging, bool Connected, DateTimeOffset ReadAtUtc, bool Stale = false);

/// <summary>One device slot of a Rongyuan receiver reply.</summary>
public readonly record struct RongyuanSlot(bool Present, int? Percent);

/// <summary>
/// Status query and reply layout of Rongyuan/Monsgeek 2.4 GHz receivers (feature report, 65 bytes, index 0 = report ID).
/// Only the read-only status query is ever sent.
/// </summary>
public static class RongyuanProtocol
{
    public const byte QueryCommand = 0xF7;
    public const int MinimumFeatureLength = 7;
    public const ushort UsagePage = 0xFFFF;
    public const ushort Usage = 0x0002;

    // Receivers the official ATTACK SHARK web driver (qmk.top) drives with this status query, by vendor ID. Taken from BatteryTray
    // (github.com/qu1ncky/BatteryTray, MIT, Providers/RongyuanDongleProvider.cs), which read them from that driver.
    static readonly (ushort Vid, ushort[] Pids)[] ReceiverIds =
    {
        (0x0DB0, new ushort[] { 0x197E, 0x8E9A }),
        (0x145F, new ushort[] { 0x0376, 0x038D }),
        (0x14A5, new ushort[] { 0xC045 }),
        (0x25AA, new ushort[] { 0x2008, 0x4008, 0x4018 }),
        (0x3151, new ushort[] { 0x4011, 0x4014, 0x4017, 0x4021, 0x4027, 0x402D, 0x4038, 0x5006, 0x5007, 0x5008, 0x5020, 0x5026, 0x502B, 0x5031, 0x5037, 0x5038, 0x503D, 0x503F, 0x5040, 0x5042, 0x5045, 0x504B, 0x504E, 0x5050, 0x5052, 0x5055, 0x505A }),
        (0x342D, new ushort[] { 0xE3E0, 0xE3E1, 0xE3E2, 0xE3E6, 0xE3E7, 0xE3E8, 0xE3F5, 0xE3F6, 0xE3F7, 0xE3FB, 0xE3FC, 0xE3FD }),
        (0x347A, new ushort[] { 0xA302, 0xA305 }),
        (0x374A, new ushort[] { 0xA205, 0xA217, 0xA220, 0xA223, 0xA226, 0xA229, 0xA234, 0xA237, 0xA251, 0xA263, 0xA268, 0xA311, 0xA314, 0xA319, 0xA322, 0xA329, 0xA355 }),
        (0x379A, new ushort[] { 0x1804, 0x1807 }),
        (0x38A9, new ushort[] { 0x0002 }),
        (0x38EE, new ushort[] { 0x0000, 0x0002, 0x0039 }),
        (0x391D, new ushort[] { 0x1A04, 0x1A08, 0x1A0E, 0x1A13, 0x1A25, 0x1A2B, 0x1A34, 0x2017, 0x2A05 }),
    };

    static readonly HashSet<uint> Receivers = ReceiverIds.SelectMany(v => v.Pids.Select(pid => ((uint)v.Vid << 16) | pid)).ToHashSet();

    public static bool IsReceiver(ushort vid, ushort pid) => Receivers.Contains(((uint)vid << 16) | pid);

    /// <summary>The set-feature buffer: report ID 0, command 0xF7, zero padded to <paramref name="featureLength"/>.</summary>
    public static byte[] StatusRequest(int featureLength)
    {
        var buffer = new byte[Math.Max(featureLength, MinimumFeatureLength)];
        buffer[1] = QueryCommand;
        return buffer;
    }

    /// <summary>
    /// Parses a reply. Returns false when the receiver did not answer (too short or all status bytes zero).
    /// A slot with a non-zero offline flag is not present; a percentage outside 1..100 means the level is unknown.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> reply, out RongyuanSlot keyboard, out RongyuanSlot mouse)
    {
        keyboard = default;
        mouse = default;
        if (reply.Length < MinimumFeatureLength) return false;
        if ((reply[2] | reply[3] | reply[4] | reply[5] | reply[6]) == 0) return false;
        keyboard = Slot(reply[2], reply[4]);
        mouse = Slot(reply[3], reply[5]);
        return true;
    }

    static RongyuanSlot Slot(byte percent, byte offline) =>
        offline != 0 ? default : new RongyuanSlot(true, percent is >= 1 and <= 100 ? percent : null);

    /// <summary>AJ-series variant: get-feature with report ID 5 after the same query. Percent 0 means linked but the device is off.</summary>
    public static bool TryParseAjSeries(ReadOnlySpan<byte> reply, out RongyuanSlot keyboard)
    {
        keyboard = default;
        if (reply.Length < 4 || reply[0] != 0x05 || reply[1] != 0 || reply[2] != 0) return false;
        var percent = reply[3];
        keyboard = percent == 0 ? default : new RongyuanSlot(true, percent <= 100 ? percent : null);
        return true;
    }

    /// <summary>The product string is useful only when it names the device rather than the dongle.</summary>
    public static bool IsGenericName(string? product) =>
        string.IsNullOrWhiteSpace(product) || product.Contains("Receiver", StringComparison.OrdinalIgnoreCase) || product.Contains("2.4G", StringComparison.OrdinalIgnoreCase);
}

/// <summary>LAMZU mice (vendors 0x373E and 0x37B0) and Arbiter Studio Akitsu mice (which share 0x373E): a 65-byte feature report exchange.</summary>
public static class LamzuProtocol
{
    public const int FeatureLength = 65;

    public static bool IsVendor(ushort vid) => vid is 0x373E or 0x37B0;

    // Every LAMZU product ID the official LAMZU Aurora web driver (lamzu.net, Config/env-models.json, app 1.0.39) knows, read for
    // interoperability: the mouse on its USB cable, and its 1K and 8K receivers. A receiver that several models share is named after
    // none of them. The Atlantis on CompX's vendor ID 3554 is read by CompxProtocol instead.
    readonly record struct Product(ushort Vid, ushort Pid, string Name, bool IsCable);

    const string SharedReceiver = "LAMZU wireless mouse";

    static readonly Product[] Products =
    {
        new(0x373E, 0x001C, "LAMZU Maya X", true),
        new(0x373E, 0x001D, "LAMZU Maya X", false),
        new(0x373E, 0x001E, "LAMZU Maya X", false),
        new(0x373E, 0x0016, "LAMZU Maya X V2", false),
        new(0x37B0, 0x0003, "LAMZU Thorn V2", false),
        new(0x37B0, 0x0005, "LAMZU Tachi", true),
        new(0x37B0, 0x0007, "LAMZU Paro", true),
        new(0x37B0, 0x0009, "LAMZU Inca", true),
        new(0x37B0, 0x000B, "LAMZU Tachi", false),
        new(0x37B0, 0x000C, "LAMZU Tachi", false),
        new(0x37B0, 0x000D, "LAMZU Paro", false),
        new(0x37B0, 0x000E, "LAMZU Paro", false),
        new(0x37B0, 0x000F, "LAMZU Inca", false),
        new(0x37B0, 0x0010, "LAMZU Inca", false),
        new(0x37B0, 0x0011, "LAMZU Maya", true),
        new(0x37B0, 0x0013, "LAMZU Maya", false),
        new(0x37B0, 0x0015, "LAMZU Maya", false),
        new(0x37B0, 0x0017, "LAMZU Thorn", true),
        new(0x37B0, 0x0019, "LAMZU Thorn", false),
        new(0x37B0, 0x001B, "LAMZU Thorn", false),
        new(0x37B0, 0x001C, "LAMZU Tachi Lite", true),
        new(0x37B0, 0x001E, "LAMZU Tachi Lite", false),
        new(0x37B0, 0x001F, "LAMZU Maya X V2", true),
        new(0x37B0, 0x0021, "LAMZU Thorn V2", true),
        new(0x37B0, 0x0023, "LAMZU Thorn V2", false),
        new(0x37B0, 0x0025, "LAMZU Atlantis OG Champion", true),
        new(0x37B0, 0x0027, "LAMZU Atlantis OG Champion", false),
        new(0x37B0, 0x0028, "LAMZU Atlantis Mini", true),
        new(0x37B0, 0x002A, "LAMZU Atlantis Mini", false),
        new(0x37B0, 0x002B, "LAMZU Atlantis Mini", false),
        new(0x37B0, 0x002C, "LAMZU Maya M", true),
        new(0x37B0, 0x002E, SharedReceiver, false),          // Maya M, Thorn V2
        new(0x37B0, 0x0030, "LAMZU Thorn V2", true),
        new(0x37B0, 0x0032, "LAMZU Thorn V2", false),
        new(0x37B0, 0x0034, "LAMZU DM198", true),
        new(0x37B0, 0x0036, SharedReceiver, false),          // DM198, DM198 OP, Maya, Orcus
        new(0x37B0, 0x0038, "LAMZU DM198 OP", true),
        new(0x37B0, 0x003A, "LAMZU Maya X", true),
        new(0x37B0, 0x003B, "LAMZU Maya X", false),
        new(0x37B0, 0x003C, "LAMZU Maya", true),
        new(0x37B0, 0x003E, "LAMZU Atlantis Mini", true),
        new(0x37B0, 0x003F, "LAMZU Atlantis Mini", false),
        new(0x37B0, 0x0040, "LAMZU Thorn V2", true),
        new(0x37B0, 0x0042, "LAMZU Orcus", true),
        new(0x37B0, 0x0044, "LAMZU Maya X", true),
        new(0x37B0, 0x0046, "LAMZU Mini", true),
        new(0x37B0, 0x0048, SharedReceiver, false),          // Maya X, Maya M, Maya, Orcus V2, LX11, Inca V2
        new(0x37B0, 0x0050, "LAMZU Maya", true),
        new(0x37B0, 0x0052, "LAMZU Maya M", true),
        new(0x37B0, 0x0054, "LAMZU Mini", false),
        new(0x37B0, 0x0056, "LAMZU Orcus V2", true),
        new(0x37B0, 0x0058, "LAMZU LX11", true),
        new(0x37B0, 0x005A, "LAMZU Inca V2", true),
    };

    // Arbiter Studio Akitsu mice on the same DriverCore vendor ID 0x373E and the same battery query (model IDs from Arbiter's web hub
    // config, as listed by Wireless-Device-Battery-Monitor). Without them an Akitsu would be named "LAMZU wireless mouse". A cable lists its
    // receiver's PID so both connections share one key.
    readonly record struct ArbiterModel(ushort Pid, string Name, bool IsCable, ushort ReceiverPid);

    static readonly ArbiterModel[] ArbiterModels =
    {
        new(0x0009, "Arbiter Studio Akitsu", true, 0x000A),
        new(0x000A, "Arbiter Studio Akitsu", false, 0x000A),
        new(0x0038, "Arbiter Studio Akitsu 3950", true, 0x0039),
        new(0x0039, "Arbiter Studio Akitsu 3950", false, 0x0039),
        new(0x0066, "Arbiter Studio Akitsu Medium", true, 0x0067),
        new(0x0067, "Arbiter Studio Akitsu Medium", false, 0x0067),
    };

    static ArbiterModel? FindArbiter(ushort vid, ushort pid) => vid == 0x373E && ArbiterModels.FirstOrDefault(m => m.Pid == pid) is { Name: not null } m ? m : null;

    static readonly Dictionary<uint, Product> ByKey = Products.ToDictionary(p => ((uint)p.Vid << 16) | p.Pid);

    static Product? Find(ushort vid, ushort pid) => ByKey.TryGetValue(((uint)vid << 16) | pid, out var p) ? p : null;

    public static bool IsArbiter(ushort vid, ushort pid) => FindArbiter(vid, pid) is not null;

    public static string ModelName(ushort vid, ushort pid) => FindArbiter(vid, pid)?.Name ?? Find(vid, pid)?.Name ?? SharedReceiver;

    public static bool IsCable(ushort vid, ushort pid) => FindArbiter(vid, pid)?.IsCable ?? Find(vid, pid)?.IsCable ?? false;

    /// <summary>The reading key: "arbiter:373E:receiver" for an Akitsu (cable and receiver share it), else "lamzu:vid:pid".</summary>
    public static string ReadingKey(ushort vid, ushort pid) =>
        FindArbiter(vid, pid) is { } a ? $"arbiter:{vid:X4}:{a.ReceiverPid:X4}" : $"lamzu:{vid:X4}:{pid:X4}";

    /// <summary>Every known LAMZU product ID (for tests and diagnostics).</summary>
    public static IReadOnlyList<(ushort Vid, ushort Pid, string Name, bool IsCable)> KnownProducts =>
        Products.Select(p => (p.Vid, p.Pid, p.Name, p.IsCable)).ToList();

    public static byte[] StatusRequest()
    {
        var buffer = new byte[FeatureLength];
        buffer[3] = 0x02;
        buffer[4] = 0x02;
        buffer[6] = 0x83;
        return buffer;
    }

    /// <summary>True for the "mouse is waking up, ask again" status (A0 instead of A1) that still echoes the request markers.</summary>
    public static bool IsWaking(ReadOnlySpan<byte> reply) =>
        reply.Length >= 10 && reply[1] == 0xA0 && reply[4] == 0x02 && reply[6] == 0x83;

    /// <summary>Accepts both reply layouts (with and without the leading report ID byte) and the answered statuses A1 and A2.</summary>
    public static bool TryParse(ReadOnlySpan<byte> reply, out bool charging, out int percent)
    {
        charging = false;
        percent = 0;
        if (reply.Length < 10) return false;
        int p;
        if (reply[1] is 0xA1 or 0xA2 && reply[4] == 0x02 && reply[6] == 0x83) { charging = reply[7] == 1; p = reply[8]; }
        else if (reply[0] is 0xA1 or 0xA2 && reply[3] == 0x02 && reply[5] == 0x83) { charging = reply[6] == 1; p = reply[7]; }
        else return false;
        if (p > 100) { charging = false; return false; }
        percent = p;
        return true;
    }

    /// <summary>A zero percentage from a dongle means the mouse is off or out of range; cable PIDs are always connected.</summary>
    public static bool IsConnected(ushort vid, ushort pid, int percent) => IsCable(vid, pid) || percent > 0;
}

/// <summary>
/// Logitech HID++ over a Unifying/Lightspeed/Bolt receiver or a cable (VID 046D). Message layout
/// <c>[report][device index][feature index][function &lt;&lt; 4 | software id][params ...]</c>, report 0x10 = short (7 bytes), 0x11 = long (20 bytes).
/// Features are discovered through the root feature, so no per-model table exists. Only read-only queries are built: ping, feature
/// lookup, name, device info and the battery getters (features 0x1004, 0x1000, 0x1001; HID++ 1.0 registers 0x0D and 0x07).
/// </summary>
public static class LogitechProtocol
{
    public const ushort Vendor = 0x046D;
    public const byte ShortReport = 0x10, LongReport = 0x11, SwId = 0x0D;
    public const int ShortLength = 7, LongLength = 20;
    public const ushort FeatureUnifiedBattery = 0x1004, FeatureBatteryStatus = 0x1000, FeatureBatteryVoltage = 0x1001, FeatureDeviceName = 0x0005, FeatureDeviceInfo = 0x0003;

    public static byte[] Request(bool isLong, byte deviceIndex, byte feature, byte function, params byte[] parameters)
    {
        var frame = new byte[isLong ? LongLength : ShortLength];
        frame[0] = isLong ? LongReport : ShortReport;
        frame[1] = deviceIndex;
        frame[2] = feature;
        frame[3] = (byte)((function << 4) | SwId);
        for (var i = 0; i < parameters.Length && 4 + i < frame.Length; i++) frame[4 + i] = parameters[i];
        return frame;
    }

    /// <summary>Root feature ping (function 1); a HID++ 2.0 device answers with its protocol version in the first parameter.</summary>
    public static byte[] Ping(bool isLong, byte deviceIndex) => Request(isLong, deviceIndex, 0x00, 1, 0, 0, 0x5A);

    /// <summary>Root feature lookup (function 0): asks for the index of <paramref name="featureId"/>.</summary>
    public static byte[] FeatureLookup(bool isLong, byte deviceIndex, ushort featureId) => Request(isLong, deviceIndex, 0x00, 0, (byte)(featureId >> 8), (byte)featureId);

    /// <summary>HID++ 1.0 register read (short GET_REGISTER, sub id 0x81).</summary>
    public static byte[] RegisterRead(byte deviceIndex, byte register) =>
        new byte[] { ShortReport, deviceIndex, 0x81, register, 0, 0, 0 };

    static bool IsHidpp(ReadOnlySpan<byte> frame, byte deviceIndex) =>
        frame.Length >= ShortLength && (frame[0] == ShortReport || frame[0] == LongReport) && frame[1] == deviceIndex;

    /// <summary>True when <paramref name="frame"/> answers the request with feature/function bytes <paramref name="b2"/>/<paramref name="b3"/>, or is its error reply.</summary>
    public static bool IsAnswer(ReadOnlySpan<byte> frame, byte deviceIndex, byte b2, byte b3) =>
        IsHidpp(frame, deviceIndex) && ((frame[2] == b2 && frame[3] == b3) || IsError(frame, deviceIndex, b2, b3));

    /// <summary>HID++ 2.0 error (<c>[..][idx][FF][feature][fn|sw][code]</c>) or HID++ 1.0 error (<c>[..][idx][8F][sub id][address][code]</c>).</summary>
    public static bool IsError(ReadOnlySpan<byte> frame, byte deviceIndex, byte b2, byte b3) =>
        IsHidpp(frame, deviceIndex) && frame.Length >= 6 && (frame[2] == 0xFF || frame[2] == 0x8F) && frame[3] == b2 && frame[4] == b3;

    public static byte ErrorCode(ReadOnlySpan<byte> frame) => frame.Length >= 6 ? frame[5] : (byte)0;

    /// <summary>Parameters of a successful reply (everything after the four header bytes), empty for an error or foreign frame.</summary>
    public static byte[] Parameters(ReadOnlySpan<byte> frame, byte deviceIndex, byte b2, byte b3) =>
        IsHidpp(frame, deviceIndex) && frame[2] == b2 && frame[3] == b3 ? frame[4..].ToArray() : Array.Empty<byte>();

    /// <summary>Index of the feature in a lookup reply, or 0 when the device does not have it.</summary>
    public static byte FeatureIndex(ReadOnlySpan<byte> parameters) => parameters.Length > 0 ? parameters[0] : (byte)0;

    /// <summary>Protocol major version in a ping reply, or 0 when it is not a HID++ 2.0 answer.</summary>
    public static int ProtocolMajor(ReadOnlySpan<byte> parameters) => parameters.Length >= 3 && parameters[2] == 0x5A ? parameters[0] : 0;

    /// <summary>Unified battery (0x1004) function 1: state of charge, level flags, status.</summary>
    public static bool TryParseUnified(ReadOnlySpan<byte> p, out int? percent, out bool charging)
    {
        percent = null; charging = false;
        if (p.Length < 3) return false;
        int soc = p[0], level = p[1], status = p[2];
        if (soc is > 0 and <= 100) percent = soc;
        else if (level != 0) percent = (level & 8) != 0 ? 100 : (level & 4) != 0 ? 60 : (level & 2) != 0 ? 20 : 5;
        charging = status is 1 or 2 or 3;
        return true;
    }

    /// <summary>Battery level status (0x1000) function 0: percent, next level, status (1-4 = charging variants, 3 = full).</summary>
    public static bool TryParseBatteryStatus(ReadOnlySpan<byte> p, out int? percent, out bool charging)
    {
        percent = null; charging = false;
        if (p.Length < 3) return false;
        var status = p[2];
        charging = status is >= 1 and <= 4;
        percent = p[0] <= 100 ? p[0] : null;
        if (percent == 0 && status == 3) percent = 100;
        return true;
    }

    /// <summary>Battery voltage (0x1001) function 0: millivolts (big endian) and a flag byte whose top bit means charging.</summary>
    public static bool TryParseVoltage(ReadOnlySpan<byte> p, out int millivolts, out int? percent, out bool charging)
    {
        millivolts = 0; percent = null; charging = false;
        if (p.Length < 3) return false;
        millivolts = (p[0] << 8) | p[1];
        charging = (p[2] & 0x80) != 0;
        if (millivolts > 0) percent = PercentFromMillivolts(millivolts);
        return true;
    }

    static readonly int[] CellMv = { 4186, 4067, 3989, 3922, 3859, 3811, 3778, 3751, 3717, 3671, 3646, 3579, 3500 };
    static readonly int[] CellPercent = { 100, 90, 80, 70, 60, 50, 40, 30, 20, 10, 5, 2, 0 };

    /// <summary>Typical single Li-ion cell discharge curve, linearly interpolated and clamped to 0..100.</summary>
    public static int PercentFromMillivolts(int mv)
    {
        if (mv >= CellMv[0]) return 100;
        if (mv <= CellMv[^1]) return 0;
        for (var i = 1; i < CellMv.Length; i++)
        {
            if (mv < CellMv[i]) continue; // find the first curve point at or below mv
            var span = CellMv[i - 1] - CellMv[i];
            return CellPercent[i] + (CellPercent[i - 1] - CellPercent[i]) * (mv - CellMv[i]) / span;
        }
        return 0;
    }

    /// <summary>HID++ 1.0 register 0x0D reply: percent, ?, charge state (0x21..0x26 = charging).</summary>
    public static bool TryParseRegisterPercent(ReadOnlySpan<byte> p, out int percent, out bool charging)
    {
        percent = 0; charging = false;
        if (p.Length < 3 || p[0] is 0 or > 100) return false;
        percent = p[0];
        charging = p[2] is >= 0x21 and <= 0x26;
        return true;
    }

    /// <summary>HID++ 1.0 register 0x07 reply: level 1..7 mapped to a coarse percentage.</summary>
    public static bool TryParseRegisterLevel(ReadOnlySpan<byte> p, out int percent, out bool charging)
    {
        percent = 0; charging = false;
        if (p.Length < 2 || p[0] == 0) return false;
        percent = new[] { 0, 5, 10, 20, 35, 50, 75, 100 }[Math.Min((int)p[0], 7)];
        charging = p[1] is >= 0x21 and <= 0x26;
        return true;
    }

    /// <summary>Device type byte of DEVICE_NAME function 2 (0 or 2 keyboard, 3 to 5 mouse/trackball, 8 headset, 11 or 12 gamepad).</summary>
    public static BatteryDeviceKind KindFromType(byte type) => type switch
    {
        0 or 2 => BatteryDeviceKind.Keyboard,
        3 or 4 or 5 => BatteryDeviceKind.Mouse,
        8 => BatteryDeviceKind.Headset,
        0x0B or 0x0C => BatteryDeviceKind.Gamepad,
        _ => BatteryDeviceKind.Other,
    };

    /// <summary>"G903 LIGHTSPEED Wireless Gaming Mouse" -> unchanged; adds the vendor when the name does not carry it.</summary>
    public static string DisplayName(string name)
    {
        var n = name.Trim();
        if (n.Length == 0) return "Logitech wireless device";
        return n.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase) || n.StartsWith("G ", StringComparison.Ordinal) || n.StartsWith("MX", StringComparison.Ordinal)
            || (n.Length > 1 && n[0] == 'G' && char.IsDigit(n[1])) ? n : "Logitech " + n;
    }

    /// <summary>Stable id of the physical device from DEVICE_INFORMATION function 0 (parameters: entity count, unit id x4), or "" when absent.</summary>
    public static string UnitId(ReadOnlySpan<byte> p) =>
        p.Length >= 5 && (p[1] | p[2] | p[3] | p[4]) != 0 ? $"{p[1]:X2}{p[2]:X2}{p[3]:X2}{p[4]:X2}" : "";
}

/// <summary>Scaling of a generic HID Battery Strength value to a percentage.</summary>
public static class HidBatteryScale
{
    public static int? Percent(int value, int logicalMin, int logicalMax, ushort vid = 0)
    {
        // Apple devices report a ready-made percentage regardless of the declared range.
        if (vid == 0x05AC && value is >= 0 and <= 100) return value;
        if (logicalMax > logicalMin) return Math.Clamp((int)((long)(value - logicalMin) * 100 / (logicalMax - logicalMin)), 0, 100);
        return value is >= 0 and <= 100 ? value : null;
    }
}

public static class BluetoothClassOfDevice
{
    public static BatteryDeviceKind Kind(uint cod)
    {
        var major = (int)((cod >> 8) & 0x1F);
        var minor = (int)((cod >> 2) & 0x3F);
        if (major == 4) return BatteryDeviceKind.Headset;
        if (major != 5) return BatteryDeviceKind.Other;
        if ((minor & 0xF) is 1 or 2) return BatteryDeviceKind.Gamepad;
        return (minor >> 4) switch { 1 => BatteryDeviceKind.Keyboard, 2 => BatteryDeviceKind.Mouse, _ => BatteryDeviceKind.Other };
    }
}

public static class DeviceBatteryMerge
{
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Combines the previous published list with a fresh poll. A connected device that vanished from the poll keeps its last
    /// reading marked stale until the grace period (measured from the original read) expires. Disconnected readings are dropped.
    /// </summary>
    public static IReadOnlyList<DeviceBatteryReading> Merge(IReadOnlyList<DeviceBatteryReading> previous, IReadOnlyList<DeviceBatteryReading> polled, DateTimeOffset now, TimeSpan? grace = null)
    {
        var limit = grace ?? DefaultGrace;
        var result = new Dictionary<string, DeviceBatteryReading>();
        foreach (var reading in polled)
            if (reading.Connected) result[reading.Key] = reading; // a driver may mark its own reading stale (Phone Link card not rewritten)
        var polledKeys = polled.Select(r => r.Key).ToHashSet();
        foreach (var old in previous)
        {
            if (polledKeys.Contains(old.Key) || !old.Connected || now - old.ReadAtUtc > limit) continue;
            result[old.Key] = old with { Stale = true };
        }
        return result.Values
            .OrderBy(r => KindOrder(r.Kind))
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static int KindOrder(BatteryDeviceKind kind) => kind switch
    {
        BatteryDeviceKind.Keyboard => 0,
        BatteryDeviceKind.Mouse => 1,
        BatteryDeviceKind.Headset => 2,
        BatteryDeviceKind.Gamepad => 3,
        BatteryDeviceKind.Phone => 4,
        _ => 5,
    };
}

/// <summary>
/// Decides when a low wireless-device battery notice should be shown. Fires once per device key when a fresh, discharging reading
/// is at or below 15%, and re-arms above 20% or while charging. A device first seen already low fires once. Stale and unknown readings are ignored.
/// </summary>
public sealed class DeviceBatteryAlertPolicy
{
    public const int LowPercent = 15;
    public const int RearmPercent = 20;

    readonly Dictionary<string, bool> armed = new();

    public DeviceBatteryReading? Evaluate(IReadOnlyList<DeviceBatteryReading> readings, DateTimeOffset now)
    {
        DeviceBatteryReading? fire = null;
        foreach (var reading in readings)
        {
            if (reading.Stale || !reading.Connected || reading.Percent is not { } percent) continue;
            if (reading.Charging || percent > RearmPercent) { armed[reading.Key] = true; continue; }
            if (percent > LowPercent) continue;
            if (armed.GetValueOrDefault(reading.Key, true) && (fire is null || percent < fire.Percent)) fire = reading;
        }
        if (fire is not null) armed[fire.Key] = false;
        return fire;
    }

    public static string Text(DeviceBatteryReading reading) => $"{KindLabel(reading.Kind)} battery {reading.Percent}%";

    static string KindLabel(BatteryDeviceKind kind) => kind switch
    {
        BatteryDeviceKind.Keyboard => "Keyboard",
        BatteryDeviceKind.Mouse => "Mouse",
        BatteryDeviceKind.Headset => "Headset",
        BatteryDeviceKind.Gamepad => "Gamepad",
        _ => "Device",
    };
}

/// <summary>Header chip of the Hardware → Battery page, which is always available (This PC and Power use cards apply to every PC).</summary>
public static class BatteryChipPolicy
{
    /// <summary>What the header chip shows: the system battery on a laptop, else the lowest wireless device, else "PSU" on a desktop.</summary>
    public static BatteryChipInfo Summary(bool hasSystemBattery, int systemPercent, bool systemCharging, IReadOnlyList<DeviceBatteryReading> devices)
    {
        if (hasSystemBattery) return new("Battery", systemPercent + "%", systemPercent, systemCharging, BatteryDeviceKind.Other);
        var lowest = devices.Where(d => d.Percent is not null).OrderBy(d => d.Percent).FirstOrDefault();
        return lowest is null
            ? new("Power", "PSU", null, false, BatteryDeviceKind.Other)
            : new("Battery", lowest.Percent + "%", lowest.Percent, lowest.Charging, lowest.Kind);
    }
}

public readonly record struct BatteryChipInfo(string Label, string Text, int? Percent, bool Charging, BatteryDeviceKind Kind);

/// <summary>
/// Battery of a phone linked with Microsoft Phone Link, read from the Start-menu companion card Phone Link keeps in its package folder
/// (an undocumented Adaptive Card). Only "speak" ("&lt;name&gt; - Connected to your PC") and the text "Battery N% remaining" are used;
/// anything else means no phone is reported. A card not rewritten for <see cref="StaleAfter"/> is stale (last known value).
/// </summary>
public static class PhoneLinkProtocol
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);
    const string Connected = " - Connected to your PC";

    public static string CardPath(string localAppData) =>
        System.IO.Path.Combine(localAppData, "Packages", "Microsoft.YourPhone_8wekyb3d8bbwe", "LocalState", "StartMenu", "StartMenuCompanion.json");

    public static bool TryParse(string json, DateTimeOffset written, DateTimeOffset now, out string name, out int percent, out bool stale)
    {
        name = ""; percent = 0; stale = false;
        if (string.IsNullOrWhiteSpace(json)) return false;
        string? speak;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            speak = FindSpeak(doc.RootElement);
        }
        catch (System.Text.Json.JsonException) { return false; }
        if (speak is null || !speak.EndsWith(Connected, StringComparison.Ordinal)) return false;
        name = speak[..^Connected.Length].Trim();
        if (name.Length == 0) return false;
        var match = System.Text.RegularExpressions.Regex.Match(json, @"Battery (\d{1,3})% remaining");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out percent) || percent > 100) return false;
        stale = now - written > StaleAfter;
        return true;
    }

    static string? FindSpeak(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "speak" && property.Value.ValueKind == System.Text.Json.JsonValueKind.String) return property.Value.GetString();
                    if (FindSpeak(property.Value) is { } found) return found;
                }
                break;
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) if (FindSpeak(item) is { } found) return found;
                break;
        }
        return null;
    }
}

/// <summary>Collapses the same physical device reported by several sources (receiver and cable, HID and Bluetooth, twin BLE nodes).</summary>
public static class DeviceBatteryDedupe
{
    public static IReadOnlyList<DeviceBatteryReading> Collapse(IReadOnlyList<DeviceBatteryReading> readings) =>
        readings
            .GroupBy(r => (Normalize(r.Name), r.Kind))
            .Select(g => g.OrderByDescending(r => r.Connected)
                .ThenBy(r => r.Stale)
                .ThenByDescending(r => r.Via.Contains("cable", StringComparison.OrdinalIgnoreCase) || r.Charging)
                .ThenBy(r => string.Equals(r.Via, "Bluetooth", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(r => r.Percent is not null)
                .ThenByDescending(r => r.ReadAtUtc)
                .First())
            .ToList();

    static string Normalize(string name) => new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}

/// <summary>Per-device hiding from the taskbar widget and the low-battery notice (hidden devices stay on the Battery page).</summary>
public static class DeviceBatteryVisibility
{
    public static IReadOnlyList<DeviceBatteryReading> Visible(IReadOnlyList<DeviceBatteryReading> readings, IEnumerable<string>? hiddenKeys)
    {
        var hidden = hiddenKeys is null ? new HashSet<string>() : new HashSet<string>(hiddenKeys, StringComparer.OrdinalIgnoreCase);
        return hidden.Count == 0 ? readings : readings.Where(r => !hidden.Contains(r.Key)).ToList();
    }
}

/// <summary>
/// Status protocol of mice on Compx-based 2.4 GHz receivers (VID 3554; LAMZU 1K/4K receivers use it): a 17-byte output report with
/// ID 08, <c>[08][cmd][0 x14][checksum]</c>, answered by an input report with the same ID and command. checksum = 0x55 - sum(bytes 0..15).
/// Command 03 returns online status in byte 6; command 04 returns the battery percent (byte 6), a charging flag (byte 7) and
/// millivolts (bytes 8-9). Both are read-only queries.
/// </summary>
public static class CompxProtocol
{
    public const int FrameLength = 17;
    public const byte ReportId = 0x08, OnlineCommand = 0x03, BatteryCommand = 0x04;

    public static byte Checksum(ReadOnlySpan<byte> frame)
    {
        var sum = 0;
        for (var i = 0; i < FrameLength - 1 && i < frame.Length; i++) sum += frame[i];
        return (byte)(0x55 - sum);
    }

    public static byte[] Request(byte command)
    {
        var frame = new byte[FrameLength];
        frame[0] = ReportId;
        frame[1] = command;
        frame[FrameLength - 1] = Checksum(frame);
        return frame;
    }

    public static bool IsReply(byte[] frame, byte command) =>
        frame.Length >= FrameLength && frame[0] == ReportId && frame[1] == command && frame[FrameLength - 1] == Checksum(frame);

    /// <summary>True when the dongle says the mouse is connected (byte 6 of the online reply).</summary>
    public static bool IsOnline(byte[] reply) => reply.Length > 6 && reply[6] != 0;

    public static bool TryParseBattery(byte[] reply, out int percent, out bool charging, out int millivolts)
    {
        percent = 0; charging = false; millivolts = 0;
        if (!IsReply(reply, BatteryCommand) || reply[6] > 100) return false;
        percent = reply[6];
        charging = reply[7] != 0;
        millivolts = (reply[8] << 8) | reply[9];
        return true;
    }
}

/// <summary>
/// Generic Areson / SinoWealth 2.4 GHz receiver 25A7:FA7C ("2.4G Wireless Receiver"; ships as the small/default dongle of LAMZU mice and
/// with the Fantech Helios Go XD5). The request is the Compx frame <c>[08][cmd][0 x14][checksum]</c> sent as a 17-byte feature report on
/// the FF02:0002 collection; the answer arrives as a 17-byte input report on FF01:0000:
/// <c>[09][cmd][00][00][00][payload length][payload ...][checksum]</c>, the whole frame summing to 0x55. Command 03 (online) has a one-byte
/// payload, 1 = the mouse is linked; command 04 (battery) has a two-byte payload: whole percent, then 1 = charging. Measured on a LAMZU Maya
/// (25% matched LAMZU's software). The receiver does not name the mouse. Both queries are read-only.
/// </summary>
public static class AresonProtocol
{
    public const ushort Vid = 0x25A7, ReceiverPid = 0xFA7C;
    public const ushort CommandUsagePage = 0xFF02, CommandUsage = 0x0002, ReplyUsagePage = 0xFF01;
    public const int FrameLength = 17;
    public const string MouseName = "Wireless mouse";

    public static bool IsReceiver(ushort vid, ushort pid) => vid == Vid && pid == ReceiverPid;

    /// <summary>The request frame (same layout and checksum as <see cref="CompxProtocol"/>).</summary>
    public static byte[] Request(byte command) => CompxProtocol.Request(command);

    /// <summary>A reply to <paramref name="command"/>: report ID 08 or 09, the command echoed, a payload that fits, and a frame summing to 0x55.</summary>
    public static bool IsReply(byte[] frame, byte command)
    {
        if (frame.Length < FrameLength || frame[0] is not (0x08 or 0x09) || frame[1] != command) return false;
        if (frame[5] is 0 or > FrameLength - 7) return false;
        var sum = 0;
        for (var i = 0; i < FrameLength; i++) sum += frame[i];
        return (byte)sum == 0x55;
    }

    public static bool IsOnline(byte[] reply) => IsReply(reply, CompxProtocol.OnlineCommand) && reply[6] != 0;

    public static bool TryParseBattery(byte[] reply, out int percent, out bool charging)
    {
        percent = 0;
        charging = false;
        if (!IsReply(reply, CompxProtocol.BatteryCommand) || reply[5] < 2 || reply[6] > 100 || reply[7] > 1) return false;
        percent = reply[6];
        charging = reply[7] == 1;
        return true;
    }
}

/// <summary>
/// 2.4 GHz dongles of SONiX-based keyboards (AJAZZ AK820 MAX, AULA F108 Pro, Epomaker Galaxy100 Lite ...): usage page FF60 / usage 61,
/// 33-byte input and output reports (report ID 0 + 32 bytes). Request <c>[00][20][01][0 ...][sum]</c> where sum is the byte sum of
/// bytes 1..31; the reply has the same layout with the level in byte 4 (<c>[00][20][01][00][level] ... [sum]</c>). A level of 0 means the
/// keyboard is off, out of range or in another mode. Read-only; the keyboard only answers while in 2.4G mode.
/// </summary>
public static class SonixProtocol
{
    public const int ReportLength = 33;
    public const ushort UsagePage = 0xFF60, Usage = 0x61;

    static byte Sum(ReadOnlySpan<byte> frame)
    {
        var sum = 0;
        for (var i = 1; i < ReportLength - 1 && i < frame.Length; i++) sum += frame[i];
        return (byte)sum;
    }

    public static byte[] StatusRequest()
    {
        var frame = new byte[ReportLength];
        frame[1] = 0x20;
        frame[2] = 0x01;
        frame[ReportLength - 1] = Sum(frame);
        return frame;
    }

    public static bool IsReply(byte[] frame) =>
        frame.Length >= ReportLength && frame[1] == 0x20 && frame[2] == 0x01 && frame[ReportLength - 1] == Sum(frame);

    /// <summary>The battery percent of a valid reply, or null when the keyboard reports no level (0, off or another mode).</summary>
    public static int? Level(byte[] reply) => IsReply(reply) && reply[4] is >= 1 and <= 100 ? reply[4] : null;

    /// <summary>"Epomaker Galaxy100 Lite-2.4G" / "AJAZZ AK820 MAX 2.4G" -> the keyboard's own name.</summary>
    public static string KeyboardName(string product)
    {
        var name = product.Trim();
        foreach (var suffix in new[] { "-2.4G", " 2.4G", "2.4G" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { name = name[..^suffix.Length].Trim(); break; }
        return name.Length == 0 ? "Wireless keyboard" : name;
    }
}
