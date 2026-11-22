using System.Collections.Concurrent;
using System.IO;
using MochiDock.Core;

namespace MochiDock.Services.DeviceBattery;

/// <summary>One open HID++ collection: a single overlapped handle reused for every request of a scan. Only requests built by <see cref="LogitechProtocol"/> are written.</summary>
sealed class HidppChannel : IDisposable
{
    readonly FileStream stream;
    readonly int inputLength;
    public bool IsLong { get; }

    HidppChannel(FileStream stream, int inputLength, bool isLong) { this.stream = stream; this.inputLength = inputLength; IsLong = isLong; }

    public static HidppChannel? Open(HidCollection collection, bool isLong)
    {
        var handle = HidNative.OpenOverlapped(collection.Path);
        if (handle.IsInvalid) { handle.Dispose(); return null; }
        try { return new HidppChannel(new FileStream(handle, FileAccess.ReadWrite, 0, true), collection.InputLength, isLong); }
        catch { handle.Dispose(); return null; }
    }

    /// <summary>Writes the request and returns the first input frame <paramref name="accept"/> takes, or null after <paramref name="timeoutMs"/>.</summary>
    public byte[]? Exchange(byte[] request, Func<byte[], bool> accept, int timeoutMs)
    {
        try
        {
            if (!HidNative.WriteWithTimeout(stream, request, timeoutMs)) return null;
            var deadline = Environment.TickCount64 + timeoutMs;
            var buffer = new byte[inputLength];
            while (true)
            {
                var left = deadline - Environment.TickCount64;
                if (left <= 0) return null;
                using var timeout = new CancellationTokenSource((int)left);
                int read;
                try { read = stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { return null; }
                if (read <= 0) return null;
                var frame = buffer.AsSpan(0, read).ToArray();
                if (accept(frame)) return frame;
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Dispose() => stream.Dispose();
}

/// <summary>
/// Logitech receivers (Lightspeed, Unifying, Bolt) and cabled devices: read-only HID++ queries on the FF00:0001 (short) and FF00:0002 (long)
/// collections. Feature indices, name and unit id are discovered once per device and cached, so later scans cost a single battery request.
/// A device that does not answer the ping (asleep or off) yields no reading and the merge keeps its last value as stale.
/// </summary>
static class LogitechDriver
{
    const int ReplyTimeoutMs = 400;
    sealed record Known(string Name, BatteryDeviceKind Kind, ushort BatteryFeature, byte BatteryIndex, string Unit);
    static readonly ConcurrentDictionary<string, Known> Cache = new();

    public static List<DeviceBatteryReading> Poll(List<HidCollection> collections, HashSet<(ushort, ushort)> claimed, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var readings = new List<DeviceBatteryReading>();
        if (cancellationToken.IsCancellationRequested) return readings;
        var candidates = collections.Where(c => c.Vid == LogitechProtocol.Vendor && c.UsagePage == 0xFF00
            && ((c.Usage == 1 && c.OutputLength == LogitechProtocol.ShortLength) || (c.Usage == 2 && c.OutputLength == LogitechProtocol.LongLength))
            && !c.Path.Contains("{00001812-", StringComparison.OrdinalIgnoreCase) && !c.Path.Contains("{00001124-", StringComparison.OrdinalIgnoreCase));
        foreach (var group in candidates.GroupBy(c => (c.Vid, c.Pid)))
        {
            claimed.Add(group.Key);
            var shortCollection = group.FirstOrDefault(c => c.Usage == 1);
            var longCollection = group.FirstOrDefault(c => c.Usage == 2);
            using var shortChannel = shortCollection is null ? null : HidppChannel.Open(shortCollection, false);
            using var longChannel = longCollection is null ? null : HidppChannel.Open(longCollection, true);
            var primary = longChannel ?? shortChannel;
            if (primary is null) continue;
            var isReceiver = (longCollection ?? shortCollection)!.Product.Contains("Receiver", StringComparison.OrdinalIgnoreCase);
            var indices = isReceiver ? new byte[] { 1, 2, 3, 4, 5, 6 } : new byte[] { 0xFF };
            foreach (var index in indices)
            {
                var reading = ReadDevice(group.Key.Pid, primary, shortChannel, index, isReceiver, now);
                if (reading is not null) readings.Add(reading);
            }
        }
        return readings;
    }

    static DeviceBatteryReading? ReadDevice(ushort pid, HidppChannel primary, HidppChannel? shortChannel, byte index, bool isReceiver, DateTimeOffset now)
    {
        var cacheKey = $"{pid:X4}:{index}";
        if (Cache.TryGetValue(cacheKey, out var known))
        {
            if (ReadBattery(primary, index, known.BatteryFeature, known.BatteryIndex) is { } cached) return Reading(pid, index, known, cached, isReceiver, now);
            Cache.TryRemove(cacheKey, out _); // the device changed or fell asleep: rediscover from the ping on this scan
        }

        var ping = primary.Exchange(LogitechProtocol.Ping(primary.IsLong, index), f => LogitechProtocol.IsAnswer(f, index, 0x00, (byte)(0x10 | LogitechProtocol.SwId)), ReplyTimeoutMs);
        if (ping is null) return null;
        if (LogitechProtocol.IsError(ping, index, 0x00, (byte)(0x10 | LogitechProtocol.SwId)))
            return LogitechProtocol.ErrorCode(ping) == 0x01 && shortChannel is not null ? ReadLegacy(pid, shortChannel, index, isReceiver, now) : null;
        if (LogitechProtocol.ProtocolMajor(LogitechProtocol.Parameters(ping, index, 0x00, (byte)(0x10 | LogitechProtocol.SwId))) < 2) return null;

        ushort batteryFeature = 0;
        byte batteryIndex = 0;
        foreach (var feature in new[] { LogitechProtocol.FeatureUnifiedBattery, LogitechProtocol.FeatureBatteryStatus, LogitechProtocol.FeatureBatteryVoltage })
        {
            var found = FeatureIndex(primary, index, feature);
            if (found == 0) continue;
            batteryFeature = feature; batteryIndex = found;
            break;
        }
        if (batteryIndex == 0) return null;

        var (name, kind) = ReadName(primary, index);
        var unit = ReadUnit(primary, index);
        var info = new Known(name, kind, batteryFeature, batteryIndex, unit);
        if (ReadBattery(primary, index, batteryFeature, batteryIndex) is not { } battery) return null;
        Cache[cacheKey] = info;
        return Reading(pid, index, info, battery, isReceiver, now);
    }

    static DeviceBatteryReading Reading(ushort pid, byte index, Known known, (int? Percent, bool Charging) battery, bool isReceiver, DateTimeOffset now)
    {
        var key = known.Unit.Length > 0 ? $"logi:{known.Unit}" : $"logi:{pid:X4}:{index}";
        return new DeviceBatteryReading(key, LogitechProtocol.DisplayName(known.Name), known.Kind, isReceiver ? "2.4 GHz receiver" : "USB cable", battery.Percent, battery.Charging, true, now);
    }

    static byte FeatureIndex(HidppChannel channel, byte index, ushort featureId)
    {
        var b3 = (byte)(0x00 | LogitechProtocol.SwId);
        var reply = channel.Exchange(LogitechProtocol.FeatureLookup(channel.IsLong, index, featureId), f => LogitechProtocol.IsAnswer(f, index, 0x00, b3), ReplyTimeoutMs);
        return reply is null ? (byte)0 : LogitechProtocol.FeatureIndex(LogitechProtocol.Parameters(reply, index, 0x00, b3));
    }

    static byte[]? Call(HidppChannel channel, byte index, byte featureIndex, byte function, params byte[] parameters)
    {
        var b3 = (byte)((function << 4) | LogitechProtocol.SwId);
        var reply = channel.Exchange(LogitechProtocol.Request(channel.IsLong, index, featureIndex, function, parameters), f => LogitechProtocol.IsAnswer(f, index, featureIndex, b3), ReplyTimeoutMs);
        if (reply is null) return null;
        var result = LogitechProtocol.Parameters(reply, index, featureIndex, b3);
        return result.Length == 0 ? null : result;
    }

    static (int? Percent, bool Charging)? ReadBattery(HidppChannel channel, byte index, ushort feature, byte featureIndex)
    {
        var p = Call(channel, index, featureIndex, feature == LogitechProtocol.FeatureUnifiedBattery ? (byte)1 : (byte)0);
        if (p is null) return null;
        switch (feature)
        {
            case LogitechProtocol.FeatureUnifiedBattery:
                return LogitechProtocol.TryParseUnified(p, out var u, out var uc) ? (u, uc) : null;
            case LogitechProtocol.FeatureBatteryStatus:
                return LogitechProtocol.TryParseBatteryStatus(p, out var s, out var sc) ? (s, sc) : null;
            default:
                return LogitechProtocol.TryParseVoltage(p, out _, out var v, out var vc) ? (v, vc) : null;
        }
    }

    static (string Name, BatteryDeviceKind Kind) ReadName(HidppChannel channel, byte index)
    {
        var feature = FeatureIndex(channel, index, LogitechProtocol.FeatureDeviceName);
        if (feature == 0) return ("", BatteryDeviceKind.Other);
        var name = new System.Text.StringBuilder();
        var count = Call(channel, index, feature, 0) is { Length: > 0 } c ? c[0] : 0;
        for (var guard = 0; name.Length < count && guard < 6; guard++)
        {
            var chunk = Call(channel, index, feature, 1, (byte)name.Length);
            if (chunk is null) break;
            var before = name.Length;
            foreach (var b in chunk) { if (name.Length >= count || b == 0) break; name.Append((char)b); }
            if (name.Length == before) break;
        }
        var kind = Call(channel, index, feature, 2) is { Length: > 0 } t ? LogitechProtocol.KindFromType(t[0]) : BatteryDeviceKind.Other;
        var text = name.ToString();
        if (kind == BatteryDeviceKind.Other)
            kind = text.Contains("Keyboard", StringComparison.OrdinalIgnoreCase) ? BatteryDeviceKind.Keyboard : text.Contains("Mouse", StringComparison.OrdinalIgnoreCase) || text.StartsWith("G", StringComparison.Ordinal) ? BatteryDeviceKind.Mouse : kind;
        return (text, kind);
    }

    static string ReadUnit(HidppChannel channel, byte index)
    {
        var feature = FeatureIndex(channel, index, LogitechProtocol.FeatureDeviceInfo);
        return feature == 0 || Call(channel, index, feature, 0) is not { } p ? "" : LogitechProtocol.UnitId(p);
    }

    // HID++ 1.0 devices (older Unifying peripherals): register 0x0D (percent) then 0x07 (coarse level) on the short collection.
    static DeviceBatteryReading? ReadLegacy(ushort pid, HidppChannel shortChannel, byte index, bool isReceiver, DateTimeOffset now)
    {
        foreach (var register in new byte[] { 0x0D, 0x07 })
        {
            var reply = shortChannel.Exchange(LogitechProtocol.RegisterRead(index, register), f => LogitechProtocol.IsAnswer(f, index, 0x81, register), ReplyTimeoutMs);
            if (reply is null || LogitechProtocol.IsError(reply, index, 0x81, register)) continue;
            var p = LogitechProtocol.Parameters(reply, index, 0x81, register);
            var ok = register == 0x0D ? LogitechProtocol.TryParseRegisterPercent(p, out var percent, out var charging) : LogitechProtocol.TryParseRegisterLevel(p, out percent, out charging);
            if (!ok) continue;
            return new DeviceBatteryReading($"logi:{pid:X4}:{index}", "Logitech wireless device", BatteryDeviceKind.Other, isReceiver ? "2.4 GHz receiver" : "USB cable", percent, charging, true, now);
        }
        return null;
    }
}
