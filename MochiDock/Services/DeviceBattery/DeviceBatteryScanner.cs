using System.IO;
using MochiDock.Core;

namespace MochiDock.Services.DeviceBattery;

/// <summary>
/// One blocking scan of every supported wireless battery source. Order: Rongyuan receivers, LAMZU mice (own, Compx and Areson receivers), Logitech HID++, SONiX keyboards, generic HID, Bluetooth.
/// Only read-only status queries are sent, and only to devices a driver recognises. Call from a background thread.
/// </summary>
public static class DeviceBatteryScanner
{
    public static IReadOnlyList<DeviceBatteryReading> PollOnce()
    {
        var now = DateTimeOffset.UtcNow;
        var readings = new List<DeviceBatteryReading>();
        List<HidCollection> collections;
        try { collections = HidNative.Enumerate(); }
        catch (Exception ex) { AppLog.Warn("DeviceBattery", ex); collections = new(); }

        var claimed = new HashSet<(ushort, ushort)>();
        Run("Rongyuan", () => readings.AddRange(Rongyuan(collections, claimed, now)));
        Run("LAMZU", () => readings.AddRange(Lamzu(collections, claimed, now)));
        Run("Compx", () => readings.AddRange(CompxLamzu(collections, claimed, now)));
        Run("Areson", () => readings.AddRange(Areson(collections, claimed, now)));
        Run("Logitech", () => readings.AddRange(LogitechDriver.Poll(collections, claimed, now)));
        Run("Sonix", () => readings.AddRange(SonixKeyboard(collections, claimed, now)));
        Run("HID", () => readings.AddRange(GenericHid(collections, claimed, now)));
        Run("Bluetooth", () => readings.AddRange(Bluetooth(readings, now)));
        Run("PhoneLink", () => readings.AddRange(PhoneLink(now)));
        return DeviceBatteryDedupe.Collapse(readings);
    }

    static void Run(string driver, Action action)
    {
        try { action(); }
        catch (Exception ex) { AppLog.Warn("DeviceBattery", new InvalidOperationException($"{driver} driver failed", ex)); }
    }

    static IEnumerable<DeviceBatteryReading> Rongyuan(List<HidCollection> collections, HashSet<(ushort, ushort)> claimed, DateTimeOffset now)
    {
        foreach (var c in collections.Where(c => RongyuanProtocol.IsReceiver(c.Vid, c.Pid) && c.Interface == 2
                     && c.UsagePage == RongyuanProtocol.UsagePage && c.Usage == RongyuanProtocol.Usage && c.FeatureLength >= RongyuanProtocol.MinimumFeatureLength))
        {
            claimed.Add((c.Vid, c.Pid));
            var request = RongyuanProtocol.StatusRequest(c.FeatureLength);
            RongyuanSlot keyboard = default, mouse = default;
            var answered = false;
            for (var attempt = 0; attempt < 3 && !answered; attempt++)
            {
                if (attempt > 0) Thread.Sleep(100);
                var reply = HidNative.Exchange(c.Path, request, 20, 0, c.FeatureLength);
                answered = reply is not null && RongyuanProtocol.TryParse(reply, out keyboard, out mouse);
            }
            if (!answered && c.Vid == 0x3151)
            {
                // AJ-series variant on the same vendor: same query, reply read with report ID 5.
                var reply = HidNative.Exchange(c.Path, request, 30, 0x05, c.FeatureLength);
                if (reply is not null && RongyuanProtocol.TryParseAjSeries(reply, out keyboard)) answered = true;
            }
            if (!answered) continue;
            var id = $"{c.Vid:X4}:{c.Pid:X4}";
            var generic = RongyuanProtocol.IsGenericName(c.Product);
            if (keyboard.Present)
            {
                var name = c.Vid == 0x3151 && c.Pid == 0x5029 ? "Epomaker Galaxy100 Lite" : generic ? "Wireless keyboard" : c.Product;
                yield return new DeviceBatteryReading($"rg:{id}:kb", name, BatteryDeviceKind.Keyboard, "2.4 GHz receiver", keyboard.Percent, false, true, now);
            }
            if (mouse.Present)
            {
                var name = generic || c.Product.Contains("keyboard", StringComparison.OrdinalIgnoreCase) ? "Wireless mouse" : c.Product;
                yield return new DeviceBatteryReading($"rg:{id}:ms", name, BatteryDeviceKind.Mouse, "2.4 GHz receiver", mouse.Percent, false, true, now);
            }
        }
    }

    static IEnumerable<DeviceBatteryReading> Lamzu(List<HidCollection> collections, HashSet<(ushort, ushort)> claimed, DateTimeOffset now)
    {
        foreach (var group in collections.Where(c => LamzuProtocol.IsVendor(c.Vid)).GroupBy(c => (c.Vid, c.Pid)))
        {
            claimed.Add(group.Key);
            var (vid, pid) = group.Key;
            foreach (var c in group.Where(c => c.FeatureLength == LamzuProtocol.FeatureLength).OrderByDescending(c => c.Interface == 2))
            {
                byte[]? reply = null;
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    reply = HidNative.Exchange(c.Path, LamzuProtocol.StatusRequest(), 100, 0, LamzuProtocol.FeatureLength);
                    if (reply is null || !LamzuProtocol.IsWaking(reply)) break;
                    Thread.Sleep(500); // status A0: the mouse is waking up, ask again
                }
                if (reply is null || !LamzuProtocol.TryParse(reply, out var charging, out var percent)) continue;
                var cable = LamzuProtocol.IsCable(vid, pid);
                yield return new DeviceBatteryReading(LamzuProtocol.ReadingKey(vid, pid), LamzuProtocol.ModelName(vid, pid), BatteryDeviceKind.Mouse,
                    cable ? "USB cable" : "2.4 GHz receiver", percent, charging, LamzuProtocol.IsConnected(vid, pid, percent), now);
                break;
            }
        }
    }

    // LAMZU mice on Compx-based receivers (VID 3554, product name "LAMZU 1K/4K Receiver"): read-only status frames on the 17-byte vendor
    // collection (FF02:0002 first). Restricted to LAMZU-named products so no other 3554 device ever receives a query.
    static IEnumerable<DeviceBatteryReading> CompxLamzu(List<HidCollection> collections, HashSet<(ushort, ushort)> claimed, DateTimeOffset now)
    {
        foreach (var group in collections.Where(c => c.Vid == 0x3554 && c.Product.Contains("LAMZU", StringComparison.OrdinalIgnoreCase)).GroupBy(c => (c.Vid, c.Pid)))
        {
            claimed.Add(group.Key);
            foreach (var c in group.Where(c => c.UsagePage >= 0xFF00 && c.InputLength == CompxProtocol.FrameLength)
                         .OrderByDescending(c => c.UsagePage == 0xFF02 && c.Usage == 2))
            {
                var online = HidNative.ExchangeOutput(c.Path, CompxProtocol.Request(CompxProtocol.OnlineCommand), c.InputLength, f => CompxProtocol.IsReply(f, CompxProtocol.OnlineCommand), 500);
                if (online is null) continue;               // this collection does not speak the protocol
                if (!CompxProtocol.IsOnline(online)) break; // receiver answers but the mouse is off or asleep: the merge keeps the last value as stale
                var reply = HidNative.ExchangeOutput(c.Path, CompxProtocol.Request(CompxProtocol.BatteryCommand), c.InputLength, f => CompxProtocol.IsReply(f, CompxProtocol.BatteryCommand), 500);
                if (reply is null || !CompxProtocol.TryParseBattery(reply, out var percent, out var charging, out _)) break;
                var name = c.Product.Replace(" Receiver", "", StringComparison.OrdinalIgnoreCase).Trim() + " mouse";
                var cable = !c.Product.Contains("Receiver", StringComparison.OrdinalIgnoreCase);
                yield return new DeviceBatteryReading($"compx:{c.Vid:X4}:{c.Pid:X4}", name, BatteryDeviceKind.Mouse, cable ? "USB cable" : "2.4 GHz receiver", percent, charging, true, now);
                break;
            }
        }
    }

    // Mice on the generic Areson receiver 25A7:FA7C (LAMZU's small/default dongle): the Compx frame goes out as a feature report on FF02:0002
    // and the answer comes back as an input report on FF01:0000. Read-only online + battery queries, sent only to this measured receiver.
    static IEnumerable<DeviceBatteryReading> Areson(List<HidCollection> collections, HashSet<(ushort, ushort)> claimed, DateTimeOffset now)
    {
        foreach (var group in collections.Where(c => AresonProtocol.IsReceiver(c.Vid, c.Pid)).GroupBy(c => (c.Vid, c.Pid)))
        {
            claimed.Add(group.Key);
            var command = group.FirstOrDefault(c => c.UsagePage == AresonProtocol.CommandUsagePage && c.Usage == AresonProtocol.CommandUsage
                                                    && c.FeatureLength == AresonProtocol.FrameLength);
            var reply = group.FirstOrDefault(c => c.UsagePage == AresonProtocol.ReplyUsagePage && c.InputLength == AresonProtocol.FrameLength);
            if (command is null || reply is null) continue;
            var online = HidNative.ExchangeFeatureToInput(command.Path, reply.Path, AresonProtocol.Request(CompxProtocol.OnlineCommand), reply.InputLength,
                f => AresonProtocol.IsReply(f, CompxProtocol.OnlineCommand), 500);
            if (online is null || !AresonProtocol.IsOnline(online)) continue; // mouse off, asleep or out of range: the merge keeps the last value as stale
            var battery = HidNative.ExchangeFeatureToInput(command.Path, reply.Path, AresonProtocol.Request(CompxProtocol.BatteryCommand), reply.InputLength,
                f => AresonProtocol.IsReply(f, CompxProtocol.BatteryCommand), 500);
            if (battery is null || !AresonProtocol.TryParseBattery(battery, out var percent, out var charging)) continue;
            yield return new DeviceBatteryReading($"areson:{group.Key.Vid:X4}:{group.Key.Pid:X4}", AresonProtocol.MouseName, BatteryDeviceKind.Mouse,
                "2.4 GHz receiver", percent, charging, true, now);
        }
    }

    // Keyboards on a SONiX-style 2.4 GHz dongle: read-only status frame on the FF60:0061 raw collection. Only products whose name says
    // "2.4G" are asked, so a keyboard on its cable or Bluetooth (same collection shape) never receives the query.
    static IEnumerable<DeviceBatteryReading> SonixKeyboard(List<HidCollection> collections, HashSet<(ushort, ushort)> claimed, DateTimeOffset now)
    {
        foreach (var c in collections.Where(c => c.UsagePage == SonixProtocol.UsagePage && c.Usage == SonixProtocol.Usage && c.InputLength == SonixProtocol.ReportLength
                     && c.Product.Contains("2.4G", StringComparison.OrdinalIgnoreCase) && !claimed.Contains((c.Vid, c.Pid))))
        {
            byte[]? reply = null;
            for (var attempt = 0; attempt < 2 && reply is null; attempt++) // the first request can go unanswered while the 2.4 GHz link wakes up
                reply = HidNative.ExchangeOutput(c.Path, SonixProtocol.StatusRequest(), c.InputLength, SonixProtocol.IsReply, 500);
            if (reply is null) continue;
            claimed.Add((c.Vid, c.Pid));
            var level = SonixProtocol.Level(reply);
            yield return new DeviceBatteryReading($"sonix:{c.Vid:X4}:{c.Pid:X4}", SonixProtocol.KeyboardName(c.Product), BatteryDeviceKind.Keyboard, "2.4 GHz receiver",
                level, false, level is not null, now);
        }
    }

    static IEnumerable<DeviceBatteryReading> GenericHid(List<HidCollection> collections, HashSet<(ushort, ushort)> claimed, DateTimeOffset now)
    {
        foreach (var group in collections.Where(c => !claimed.Contains((c.Vid, c.Pid))).GroupBy(c => (c.Vid, c.Pid)))
        {
            foreach (var c in group.Where(c => c.UsagePage is not (0x84 or 0x85) && (c.FeatureValueCaps > 0 || c.InputValueCaps > 0)))
            {
                var value = HidNative.ReadStandardBattery(c);
                if (value is not { } v) continue;
                var kind = group.Any(x => x.UsagePage == 1 && x.Usage == 6) ? BatteryDeviceKind.Keyboard
                    : group.Any(x => x.UsagePage == 1 && x.Usage == 2) ? BatteryDeviceKind.Mouse : BatteryDeviceKind.Other;
                var name = group.Select(x => x.Product).FirstOrDefault(p => p.Length > 0) ?? "HID device";
                claimed.Add(group.Key);
                yield return new DeviceBatteryReading($"hid:{group.Key.Vid:X4}:{group.Key.Pid:X4}", name, kind, "HID",
                    HidBatteryScale.Percent(v.Value, v.Min, v.Max, group.Key.Vid), false, true, now);
                break;
            }
        }
    }

    // A phone linked with Microsoft Phone Link: read-only parse of the companion card file (absent when Phone Link is not set up).
    static IEnumerable<DeviceBatteryReading> PhoneLink(DateTimeOffset now)
    {
        var path = PhoneLinkProtocol.CardPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is 0 or > 4 * 1024 * 1024) yield break;
        string json;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8)) json = reader.ReadToEnd();
        if (!PhoneLinkProtocol.TryParse(json, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), now, out var name, out var percent, out var stale)) yield break;
        yield return new DeviceBatteryReading($"phone:{name}", name, BatteryDeviceKind.Phone, "Phone Link", percent, false, true, now, stale);
    }

    static readonly string[] NameSuffixes = { " Hands-Free AG", " Hands-Free", " Avrcp Transport", " Stereo", " Audio Gateway" };

    static IEnumerable<DeviceBatteryReading> Bluetooth(List<DeviceBatteryReading> already, DateTimeOffset now)
    {
        var known = already.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in HidNative.EnumerateBluetooth().Where(n => !n.HasProblem && n.Container != Guid.Empty).GroupBy(n => n.Container))
        {
            var battery = group.Select(n => n.Battery).FirstOrDefault(b => b is not null);
            if (battery is null) continue;
            var main = group.FirstOrDefault(n => n.InstanceId.Contains("\\DEV_", StringComparison.OrdinalIgnoreCase) && !n.InstanceId.StartsWith("BTHLEDEVICE", StringComparison.OrdinalIgnoreCase));
            var connected = main?.Connected ?? group.Select(n => n.Connected).FirstOrDefault(c => c is not null);
            var name = (main ?? group.First()).FriendlyName ?? "Bluetooth device";
            foreach (var suffix in NameSuffixes) if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name[..^suffix.Length];
            if (known.Contains(name)) continue;
            var cod = group.Select(n => n.ClassOfDevice).FirstOrDefault(c => c is not null);
            yield return new DeviceBatteryReading($"bt:{group.Key}", name, cod is { } code ? BluetoothClassOfDevice.Kind(code) : BatteryDeviceKind.Other,
                "Bluetooth", battery, false, connected == true, now); // reported even when disconnected so the merge drops a stale copy at once
        }
    }
}
