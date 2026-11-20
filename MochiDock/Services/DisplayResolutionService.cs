namespace MochiDock.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MochiDock.Core;

public sealed record DisplayModeInfo(int Width, int Height, int RefreshRate, int BitsPerPixel)
{
    public string AspectRatio => DisplayResolutionPolicy.GetAspectRatio(Width, Height);
    public string Label => DisplayResolutionPolicy.FormatLabel(Width, Height);
}

public class DisplayResolutionService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODEA
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern bool EnumDisplaySettingsA(string? lpszDeviceName, int iModeNum, ref DEVMODEA lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int ChangeDisplaySettingsExA(string? lpszDeviceName, ref DEVMODEA lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint CDS_UPDATEREGISTRY = 0x00000001;
    private const uint CDS_TEST = 0x00000002;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const uint DM_PELSWIDTH = 0x00080000;
    private const uint DM_PELSHEIGHT = 0x00100000;
    private const uint DM_DISPLAYFREQUENCY = 0x00400000;
    private const uint DM_BITSPERPEL = 0x00040000;

    public event Action<int, int, int>? ResolutionChanged;

    public DisplayModeInfo? GetCurrentMode(string? deviceName = null)
    {
        var dm = new DEVMODEA { dmSize = (ushort)Marshal.SizeOf<DEVMODEA>() };
        if (EnumDisplaySettingsA(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
        {
            return new DisplayModeInfo((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency, (int)dm.dmBitsPerPel);
        }
        return null;
    }

    public IReadOnlyList<DisplayModeInfo> GetSupportedModes(string? deviceName = null)
    {
        var list = new List<DisplayModeInfo>();
        var seen = new HashSet<string>();
        var dm = new DEVMODEA { dmSize = (ushort)Marshal.SizeOf<DEVMODEA>() };
        int i = 0;
        while (EnumDisplaySettingsA(deviceName, i, ref dm))
        {
            string key = $"{dm.dmPelsWidth}x{dm.dmPelsHeight}@{dm.dmDisplayFrequency}";
            if (seen.Add(key))
            {
                list.Add(new DisplayModeInfo((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency, (int)dm.dmBitsPerPel));
            }
            i++;
        }
        return list;
    }

    public bool ApplyResolution(int width, int height, string? deviceName = null)
    {
        var current = GetCurrentMode(deviceName);
        if (current == null) return false;
        if (current.Width == width && current.Height == height) return true;

        // Try to match the exact refresh rate of the current mode (e.g. 200Hz)
        uint targetFreq = (uint)current.RefreshRate;
        uint targetBpp = (uint)(current.BitsPerPixel > 0 ? current.BitsPerPixel : 32);

        // Find best matching mode from supported list for this monitor
        var supported = GetSupportedModes(deviceName);
        var match = supported.FirstOrDefault(m => m.Width == width && m.Height == height && m.RefreshRate == current.RefreshRate)
                    ?? supported.Where(m => m.Width == width && m.Height == height).OrderByDescending(m => m.RefreshRate).FirstOrDefault();

        if (match != null)
        {
            targetFreq = (uint)match.RefreshRate;
            targetBpp = (uint)match.BitsPerPixel;
        }

        var devMode = new DEVMODEA
        {
            dmSize = (ushort)Marshal.SizeOf<DEVMODEA>(),
            dmPelsWidth = (uint)width,
            dmPelsHeight = (uint)height,
            dmDisplayFrequency = targetFreq,
            dmBitsPerPel = targetBpp,
            dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_BITSPERPEL
        };

        int test = ChangeDisplaySettingsExA(deviceName, ref devMode, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (test != DISP_CHANGE_SUCCESSFUL)
        {
            // If strict frequency failed, try without pinning frequency
            devMode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL;
        }

        int result = ChangeDisplaySettingsExA(deviceName, ref devMode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (result == DISP_CHANGE_SUCCESSFUL)
        {
            ResolutionChanged?.Invoke(width, height, (int)devMode.dmDisplayFrequency);
            return true;
        }

        return false;
    }

    // The resolution each monitor had before the toggle moved it to the gaming one, so the toggle goes back to exactly that.
    private readonly Dictionary<string, (int Width, int Height)> _beforeToggle = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Switches between the preferred gaming resolution and the one the monitor had before (or, when that is not known, its native
    /// one: the largest supported mode). <paramref name="fallbackWidth"/> and <paramref name="fallbackHeight"/> are used only when
    /// the monitor reports no modes at all. (The screen's current size must never be passed as "native": after the first toggle it
    /// is the gaming size, and the toggle back would change nothing.)
    /// </summary>
    public (bool Success, int NewWidth, int NewHeight, int RefreshRate) ToggleResolution(
        string? preferredGamingRes,
        int fallbackWidth = 2560,
        int fallbackHeight = 1440,
        string? deviceName = null)
    {
        var current = GetCurrentMode(deviceName);
        int curW = current?.Width ?? 0;
        int curH = current?.Height ?? 0;
        string key = deviceName ?? "";

        bool hasPreferred = DisplayResolutionPolicy.TryParse(preferredGamingRes, out int prefW, out int prefH);
        var native = DisplayResolutionPolicy.PickNative(GetSupportedModes(deviceName).Select(m => (m.Width, m.Height)), fallbackWidth, fallbackHeight);
        var back = native;
        if (_beforeToggle.TryGetValue(key, out var before) && !(hasPreferred && before.Width == prefW && before.Height == prefH)) back = before;

        var (targetW, targetH) = DisplayResolutionPolicy.GetToggleResolution(curW, curH, back.Width, back.Height, preferredGamingRes);
        // Leaving any other resolution for the gaming one: remember it for the way back.
        if (hasPreferred && targetW == prefW && targetH == prefH && !(curW == prefW && curH == prefH) && curW > 0) _beforeToggle[key] = (curW, curH);
        bool success = ApplyResolution(targetW, targetH, deviceName);
        var updated = GetCurrentMode(deviceName);
        return (success, updated?.Width ?? targetW, updated?.Height ?? targetH, updated?.RefreshRate ?? current?.RefreshRate ?? 200);
    }
}

