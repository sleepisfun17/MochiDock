using System.Runtime.InteropServices;
using MochiDock.Core;
using Microsoft.Win32.SafeHandles;

namespace MochiDock.Services;

/// <summary>
/// Reads the laptop battery's charge or discharge rate (milliwatts) through the battery class driver
/// (IOCTL_BATTERY_QUERY_TAG / IOCTL_BATTERY_QUERY_STATUS). While running on battery the rate is the whole machine's draw.
/// Read-only, no administrator rights needed. Every failure returns null (shown as unavailable).
/// </summary>
public static class BatteryPowerReader
{
    static readonly Guid GuidDeviceBattery = new("72631E54-78A4-11D0-BCF7-00AA00B7B32A");
    const uint IoctlBatteryQueryTag = 0x294040, IoctlBatteryQueryStatus = 0x294044;
    const uint DigcfPresent = 0x2, DigcfDeviceInterface = 0x10, GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareRw = 3, OpenExisting = 3;
    static bool _logged;

    [StructLayout(LayoutKind.Sequential)]
    struct DeviceInterfaceData { public int Size; public Guid Class; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    struct WaitStatus { public uint Tag, Timeout, PowerState, LowCapacity, HighCapacity; }
    [StructLayout(LayoutKind.Sequential)]
    struct BatteryStatusInfo { public uint PowerState, Capacity, Voltage; public int Rate; }

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid classGuid, uint index, ref DeviceInterfaceData data);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData data, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint code, ref uint input, int inputSize, out uint output, int outputSize, out int returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint code, ref WaitStatus input, int inputSize, out BatteryStatusInfo output, int outputSize, out int returned, IntPtr overlapped);

    /// <summary>The summed rate of all present batteries in milliwatts (negative = discharging), or null when none can be read.</summary>
    public static int? ReadRateMilliwatts()
    {
        try
        {
            var classGuid = GuidDeviceBattery;
            var set = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return null;
            try
            {
                long total = 0;
                var any = false;
                for (uint index = 0; ; index++)
                {
                    var data = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref classGuid, index, ref data)) break;
                    var path = DevicePath(set, ref data);
                    if (path is null) continue;
                    if (ReadOne(path) is { } rate && rate != BatteryRate.UnknownRate) { total += rate; any = true; }
                }
                return any ? (int)Math.Clamp(total, int.MinValue + 1, int.MaxValue) : null;
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
        catch (Exception ex)
        {
            if (!_logged) { _logged = true; AppLog.Warn("Power", ex); }
            return null;
        }
    }

    static string? DevicePath(IntPtr set, ref DeviceInterfaceData data)
    {
        SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
        if (required <= 0 || required > 4096) return null;
        var buffer = Marshal.AllocHGlobal(required);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA: cbSize is 8 on 64-bit and 6 on 32-bit; the path follows at offset 4.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            return SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, out _, IntPtr.Zero) ? Marshal.PtrToStringUni(buffer + 4) : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    static int? ReadOne(string path)
    {
        using var handle = CreateFile(path, GenericRead | GenericWrite, ShareRw, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        uint wait = 0;
        if (!DeviceIoControl(handle, IoctlBatteryQueryTag, ref wait, 4, out var tag, 4, out _, IntPtr.Zero) || tag == 0) return null;
        var query = new WaitStatus { Tag = tag };
        if (!DeviceIoControl(handle, IoctlBatteryQueryStatus, ref query, Marshal.SizeOf<WaitStatus>(), out var status, Marshal.SizeOf<BatteryStatusInfo>(), out _, IntPtr.Zero)) return null;
        return status.Rate;
    }
}
