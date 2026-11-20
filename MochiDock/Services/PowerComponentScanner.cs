using System.Management;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>
/// Finds the parts of this PC that have no power sensor (motherboard, memory sticks, drives) with read-only WMI queries and gives each a typical
/// wattage. Fans, pumps, RGB and USB devices cannot be detected reliably, so those are added by the user. Never runs in the sampling path.
/// </summary>
public static class PowerComponentScanner
{
    public static List<PowerComponent> Scan()
    {
        var found = new List<PowerComponent>();
        Safe("board", () => found.Add(Board()));
        Safe("memory", () => found.AddRange(Memory()));
        Safe("drives", () => found.AddRange(Drives()));
        return found;
    }

    static void Safe(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { AppLog.Warn("PowerComponents", $"Could not scan {what}: {ex.Message}"); }
    }

    static PowerComponent Board()
    {
        string name = "Motherboard + chipset";
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
            foreach (var board in searcher.Get())
                if (board["Product"] as string is { Length: > 0 } product) { name = "Motherboard (" + product.Trim() + ")"; break; }
        }
        catch (Exception ex) { AppLog.Warn("PowerComponents", ex.Message); }
        return new PowerComponent { Id = "board", Name = name, Kind = PowerComponentKind.Motherboard, Watts = PowerComponentDefaults.Watts(PowerComponentKind.Motherboard), Detected = true };
    }

    static IEnumerable<PowerComponent> Memory()
    {
        var index = 0;
        using var searcher = new ManagementObjectSearcher("SELECT Capacity, SMBIOSMemoryType, DeviceLocator FROM Win32_PhysicalMemory");
        foreach (var stick in searcher.Get())
        {
            index++;
            var type = Convert.ToInt32(stick["SMBIOSMemoryType"] ?? 0);
            var ddr5 = type == 34;
            var gb = Convert.ToInt64(stick["Capacity"] ?? 0L) / (1024.0 * 1024 * 1024);
            var kind = ddr5 ? "DDR5" : type == 26 ? "DDR4" : "RAM";
            var slot = (stick["DeviceLocator"] as string)?.Trim();
            yield return new PowerComponent
            {
                Id = "ram:" + (string.IsNullOrEmpty(slot) ? index.ToString() : slot),
                Name = $"{kind} {gb:0} GB" + (string.IsNullOrEmpty(slot) ? "" : " (" + slot + ")"),
                Kind = PowerComponentKind.Memory,
                Watts = PowerComponentDefaults.Watts(PowerComponentKind.Memory, ddr5),
                Detected = true,
            };
        }
    }

    static IEnumerable<PowerComponent> Drives()
    {
        var disks = new List<PowerComponent>();
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT FriendlyName, MediaType, BusType, Size, DeviceId FROM MSFT_PhysicalDisk"));
            foreach (var disk in searcher.Get())
            {
                var name = (disk["FriendlyName"] as string)?.Trim() ?? "Drive";
                var media = Convert.ToInt32(disk["MediaType"] ?? 0);   // 3 = HDD, 4 = SSD
                var bus = Convert.ToInt32(disk["BusType"] ?? 0);        // 17 = NVMe
                var sizeBytes = Convert.ToInt64(disk["Size"] ?? 0L);
                disks.Add(Disk("disk:" + (disk["DeviceId"] ?? name), name, media, bus, sizeBytes));
            }
        }
        catch (Exception ex) { AppLog.Warn("PowerComponents", "Storage namespace unavailable, falling back to Win32_DiskDrive: " + ex.Message); }
        if (disks.Count > 0) return disks;

        using var fallback = new ManagementObjectSearcher("SELECT Model, InterfaceType, MediaType, Size, Index FROM Win32_DiskDrive");
        foreach (var disk in fallback.Get())
        {
            var name = (disk["Model"] as string)?.Trim() ?? "Drive";
            var nvme = name.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || (disk["InterfaceType"] as string) == "SCSI" && name.Contains("SSD", StringComparison.OrdinalIgnoreCase);
            var fixedMedia = (disk["MediaType"] as string) ?? "";
            var ssd = nvme || name.Contains("SSD", StringComparison.OrdinalIgnoreCase) || fixedMedia.Contains("SSD", StringComparison.OrdinalIgnoreCase);
            disks.Add(Disk("disk:" + (disk["Index"] ?? name), name, ssd ? 4 : 3, nvme ? 17 : 11, Convert.ToInt64(disk["Size"] ?? 0L)));
        }
        return disks;
    }

    static PowerComponent Disk(string id, string name, int mediaType, int busType, long sizeBytes)
    {
        PowerComponentKind kind;
        if (busType == 17) kind = PowerComponentKind.Nvme;
        else if (mediaType == 4) kind = PowerComponentKind.SataSsd;
        else kind = sizeBytes > 1_200_000_000_000L ? PowerComponentKind.Hdd35 : PowerComponentKind.Hdd25; // spinning disks over about 1 TB are normally 3.5"
        return new PowerComponent { Id = id, Name = name, Kind = kind, Watts = PowerComponentDefaults.Watts(kind), Detected = true };
    }
}
