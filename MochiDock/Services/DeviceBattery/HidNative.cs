using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MochiDock.Services.DeviceBattery;

/// <summary>One top-level HID collection as seen by the Windows HID stack.</summary>
public sealed record HidCollection(string Path, ushort Vid, ushort Pid, int Interface, ushort UsagePage, ushort Usage,
    int InputLength, int FeatureLength, int InputValueCaps, int FeatureValueCaps, string Product, int OutputLength = 0);

public readonly record struct HidValueCap(ushort UsagePage, ushort Usage, byte ReportId, ushort LinkCollection, int LogicalMin, int LogicalMax, bool Feature);

/// <summary>Thin P/Invoke layer over hid.dll and setupapi.dll. Enumeration and reads use access-0 handles; the status query needs read/write access but always shares the device.</summary>
public static class HidNative
{
    const uint DIGCF_PRESENT = 2, DIGCF_ALLCLASSES = 4, DIGCF_DEVICEINTERFACE = 0x10;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    const int HidpInput = 0, HidpFeature = 2;
    const int StatusSuccess = 0x110000;

    [StructLayout(LayoutKind.Sequential)] struct SpDeviceInterfaceData { public uint Size; public Guid InterfaceClass; public uint Flags; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct SpDevInfoData { public uint Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct HidAttributes { public uint Size; public ushort VendorId, ProductId, VersionNumber; }
    [StructLayout(LayoutKind.Sequential)]
    struct HidpCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct HidpValueCaps
    {
        public ushort UsagePage; public byte ReportId, IsAlias; public ushort BitField, LinkCollection, LinkUsage, LinkUsagePage;
        public byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute, HasNull, Reserved; public ushort BitSize, ReportCount;
        public ushort R1, R2, R3, R4, R5; public uint UnitsExp, Units; public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
        public ushort UsageMin, UsageMax, StringMin, StringMax, DesignatorMin, DesignatorMax, DataIndexMin, DataIndexMax;
    }
    [StructLayout(LayoutKind.Sequential)] struct DevPropKey { public Guid Fmtid; public uint Pid; }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, [Out] HidpValueCaps[] caps, ref ushort length, IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, IntPtr data, byte[] report, uint length);
    [DllImport("hid.dll")] static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] buffer, uint length);
    [DllImport("hid.dll")] static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] buffer, uint length);
    [DllImport("hid.dll")] static extern bool HidD_GetInputReport(SafeFileHandle handle, byte[] buffer, uint length);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle handle, byte[] buffer, uint length);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SetupDiGetClassDevsW(IntPtr guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref SpDeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, uint size, out uint required, IntPtr info);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SpDevInfoData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SpDevInfoData data, StringBuilder id, uint size, out uint required);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SpDevInfoData data, ref DevPropKey key, out uint type, byte[] buffer, uint size, out uint required, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    /// <summary>Lists every present HID top-level collection that can be opened.</summary>
    public static List<HidCollection> Enumerate()
    {
        var result = new List<HidCollection>();
        HidD_GetHidGuid(out var hid);
        var set = SetupDiGetClassDevsW(ref hid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) return result;
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new SpDeviceInterfaceData { Size = (uint)Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, index, ref data)) break;
                var path = InterfacePath(set, ref data);
                if (path is null) continue;
                using var handle = Open(path);
                if (handle.IsInvalid) continue;
                var attributes = new HidAttributes { Size = (uint)Marshal.SizeOf<HidAttributes>() };
                if (!HidD_GetAttributes(handle, ref attributes)) continue;
                if (!HidD_GetPreparsedData(handle, out var preparsed)) continue;
                try
                {
                    if (HidP_GetCaps(preparsed, out var caps) != StatusSuccess) continue;
                    result.Add(new HidCollection(path, attributes.VendorId, attributes.ProductId, InterfaceNumber(path), caps.UsagePage, caps.Usage,
                        caps.InputReportByteLength, caps.FeatureReportByteLength, caps.NumberInputValueCaps, caps.NumberFeatureValueCaps, ProductString(handle), caps.OutputReportByteLength));
                }
                finally { HidD_FreePreparsedData(preparsed); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    static string? InterfacePath(IntPtr set, ref SpDeviceInterfaceData data)
    {
        SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
        if (required == 0) return null;
        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
            return SetupDiGetDeviceInterfaceDetailW(set, ref data, buffer, required, out _, IntPtr.Zero) ? Marshal.PtrToStringUni(buffer + 4) : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static SafeFileHandle Open(string path, uint access = 0) => CreateFileW(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

    // HidD_SetFeature fails with ERROR_INVALID_PARAMETER on an access-0 handle (verified on real hardware); sharing stays read|write so the OS keeps using the device.
    const uint GenericReadWrite = 0xC0000000;

    static int InterfaceNumber(string path)
    {
        var at = path.IndexOf("&mi_", StringComparison.OrdinalIgnoreCase);
        return at >= 0 && path.Length >= at + 6 && int.TryParse(path.AsSpan(at + 4, 2), System.Globalization.NumberStyles.HexNumber, null, out var mi) ? mi : -1;
    }

    static string ProductString(SafeFileHandle handle)
    {
        var buffer = new byte[256];
        return HidD_GetProductString(handle, buffer, (uint)buffer.Length) ? Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim() : "";
    }

    /// <summary>Sends a feature report, then reads one back after <paramref name="delayMs"/>. <paramref name="readId"/> is the report ID of the reply buffer.</summary>
    public static byte[]? Exchange(string path, byte[] request, int delayMs, byte readId, int length)
    {
        using var handle = Open(path, GenericReadWrite);
        if (handle.IsInvalid || !HidD_SetFeature(handle, request, (uint)request.Length)) return null;
        Thread.Sleep(delayMs);
        var reply = new byte[length];
        reply[0] = readId;
        return HidD_GetFeature(handle, reply, (uint)reply.Length) ? reply : null;
    }

    const uint FileFlagOverlapped = 0x40000000;

    /// <summary>Opens a collection for overlapped read/write, sharing it with other apps. Caller disposes the handle.</summary>
    public static SafeFileHandle OpenOverlapped(string path) =>
        CreateFileW(path, GenericReadWrite, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FileFlagOverlapped, IntPtr.Zero);

    /// <summary>
    /// Sends an output report (request[0] = report ID) and reads input reports until <paramref name="accept"/> takes one or
    /// <paramref name="timeoutMs"/> passes. Reports that do not match (other traffic on the collection) are skipped.
    /// </summary>
    public static byte[]? ExchangeOutput(string path, byte[] request, int inputLength, Func<byte[], bool> accept, int timeoutMs)
    {
        using var handle = CreateFileW(path, GenericReadWrite, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid || inputLength <= 0) return null;
        try
        {
            using var stream = new FileStream(handle, FileAccess.ReadWrite, 0, true);
            stream.Write(request, 0, request.Length);
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

    /// <summary>
    /// Sends a feature report on <paramref name="commandPath"/> and reads input reports from <paramref name="replyPath"/> (another collection
    /// of the same device) until <paramref name="accept"/> takes one or <paramref name="timeoutMs"/> passes. The reply handle is opened
    /// first, so its fresh input queue holds only reports that arrive after the request.
    /// </summary>
    public static byte[]? ExchangeFeatureToInput(string commandPath, string replyPath, byte[] request, int inputLength, Func<byte[], bool> accept, int timeoutMs)
    {
        if (inputLength <= 0) return null;
        using var replyHandle = OpenOverlapped(replyPath);
        if (replyHandle.IsInvalid) return null;
        try
        {
            using var stream = new FileStream(replyHandle, FileAccess.Read, 0, true);
            using (var command = Open(commandPath, GenericReadWrite))
                if (command.IsInvalid || !HidD_SetFeature(command, request, (uint)request.Length)) return null;
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

    /// <summary>Reads a standard HID Battery Strength value (usage page 0x06/0x20 or 0x85/0x65) from a collection, feature reports first.</summary>
    public static (int Value, int Min, int Max)? ReadStandardBattery(HidCollection collection)
    {
        using var handle = Open(collection.Path);
        if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out var preparsed)) return null;
        try
        {
            foreach (var feature in new[] { true, false })
            {
                var cap = ValueCaps(preparsed, feature, feature ? collection.FeatureValueCaps : collection.InputValueCaps).FirstOrDefault(IsBatteryCap);
                if (cap.UsagePage == 0) continue;
                var length = feature ? collection.FeatureLength : collection.InputLength;
                if (length <= 0) continue;
                var report = new byte[length];
                report[0] = cap.ReportId;
                if (!(feature ? HidD_GetFeature(handle, report, (uint)length) : HidD_GetInputReport(handle, report, (uint)length))) continue;
                if (HidP_GetUsageValue(feature ? HidpFeature : HidpInput, cap.UsagePage, cap.LinkCollection, cap.Usage, out var value, preparsed, report, (uint)length) != StatusSuccess) continue;
                return ((int)value, cap.LogicalMin, cap.LogicalMax);
            }
            return null;
        }
        finally { HidD_FreePreparsedData(preparsed); }
    }

    static bool IsBatteryCap(HidValueCap cap) => (cap.UsagePage == 0x06 && cap.Usage == 0x20) || (cap.UsagePage == 0x85 && cap.Usage == 0x65);

    static List<HidValueCap> ValueCaps(IntPtr preparsed, bool feature, int count)
    {
        var list = new List<HidValueCap>();
        if (count <= 0) return list;
        var caps = new HidpValueCaps[count];
        var length = (ushort)count;
        if (HidP_GetValueCaps(feature ? HidpFeature : HidpInput, caps, ref length, preparsed) != StatusSuccess) return list;
        for (var i = 0; i < length; i++)
        {
            var c = caps[i];
            // A range cap covers several usages; report the battery usage when the range contains it.
            ushort wanted = c.UsagePage == 0x06 ? (ushort)0x20 : c.UsagePage == 0x85 ? (ushort)0x65 : c.UsageMin;
            ushort usage = c.IsRange != 0 && c.UsageMin <= wanted && wanted <= c.UsageMax ? wanted : c.UsageMin;
            list.Add(new HidValueCap(c.UsagePage, usage, c.ReportId, c.LinkCollection, c.LogicalMin, c.LogicalMax, feature));
        }
        return list;
    }

    /// <summary>Battery properties of present Bluetooth device nodes, grouped by container by the caller.</summary>
    public sealed record BluetoothNode(string InstanceId, Guid Container, int? Battery, bool? Connected, uint? ClassOfDevice, string? FriendlyName, bool HasProblem);

    static readonly Guid BatteryFmt = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5");
    static readonly Guid ConnectedFmt = new("83DA6326-97A6-4088-9453-A1923F573B29");
    static readonly Guid CodFmt = new("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A");
    static readonly Guid ContainerFmt = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C");
    static readonly Guid FriendlyFmt = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");

    public static List<BluetoothNode> EnumerateBluetooth()
    {
        var result = new List<BluetoothNode>();
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
        if (set == new IntPtr(-1)) return result;
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new SpDevInfoData { Size = (uint)Marshal.SizeOf<SpDevInfoData>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref info)) break;
                var id = new StringBuilder(512);
                if (!SetupDiGetDeviceInstanceIdW(set, ref info, id, (uint)id.Capacity, out _)) continue;
                var instance = id.ToString();
                if (!(instance.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase) || instance.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase) || instance.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase))) continue;
                var container = Property(set, ref info, ContainerFmt, 2) is { Length: 16 } g ? new Guid(g) : Guid.Empty;
                var battery = Property(set, ref info, BatteryFmt, 2) is { Length: >= 1 } b ? (int?)b[0] : null;
                var connected = Property(set, ref info, ConnectedFmt, 15) is { Length: >= 1 } c ? (bool?)(c[0] != 0) : null;
                var cod = Property(set, ref info, CodFmt, 10) is { Length: >= 4 } k ? (uint?)BitConverter.ToUInt32(k, 0) : null;
                var name = Property(set, ref info, FriendlyFmt, 14) is { Length: > 2 } n ? Encoding.Unicode.GetString(n).TrimEnd('\0') : null;
                var problem = CM_Get_DevNode_Status(out _, out var code, info.DevInst, 0) == 0 && code != 0;
                result.Add(new BluetoothNode(instance, container, battery is >= 0 and <= 100 ? battery : null, connected, cod, name, problem));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    static byte[]? Property(IntPtr set, ref SpDevInfoData info, Guid format, uint pid)
    {
        var key = new DevPropKey { Fmtid = format, Pid = pid };
        var buffer = new byte[512];
        if (!SetupDiGetDevicePropertyW(set, ref info, ref key, out _, buffer, (uint)buffer.Length, out var required, 0)) return null;
        return buffer.AsSpan(0, (int)Math.Min(required, (uint)buffer.Length)).ToArray();
    }
}
