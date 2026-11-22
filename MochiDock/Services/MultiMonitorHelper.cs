using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace MochiDock.Services;

public readonly record struct MonitorRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

public sealed class MonitorScreen
{
    public string DeviceName { get; init; } = "";
    public MonitorRect Bounds { get; init; }
    public MonitorRect WorkingArea { get; init; }
    public bool Primary { get; init; }
    internal IntPtr Handle { get; init; }
}

public class MonitorOption
{
    public string DeviceName { get; set; } = "";
    public string DisplayLabel { get; set; } = "";
    public bool IsPrimary { get; set; }
    public MonitorScreen Screen { get; set; } = null!;
}

public static class MultiMonitorHelper
{
    private const uint MONITORINFOF_PRIMARY = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private static MonitorScreen? ReadMonitor(IntPtr hMonitor)
    {
        var info = new MONITORINFOEXW { cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>() };
        if (!GetMonitorInfoW(hMonitor, ref info)) return null;
        return new MonitorScreen
        {
            DeviceName = info.szDevice ?? "",
            Bounds = new MonitorRect(info.rcMonitor.Left, info.rcMonitor.Top,
                info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top),
            WorkingArea = new MonitorRect(info.rcWork.Left, info.rcWork.Top,
                info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top),
            Primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
            Handle = hMonitor
        };
    }

    private static List<MonitorScreen> EnumerateScreens()
    {
        var screens = new List<MonitorScreen>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT _, IntPtr _) =>
        {
            if (ReadMonitor(hMon) is { } screen) screens.Add(screen);
            return true;
        }, IntPtr.Zero);
        return screens;
    }

    public static MonitorScreen? FromHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        IntPtr hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        return hMon != IntPtr.Zero ? ReadMonitor(hMon) : null;
    }

    public static List<MonitorOption> GetAllMonitors()
    {
        var list = new List<MonitorOption>();
        var screens = EnumerateScreens();

        for (int i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            string primaryTag = s.Primary ? " (Primary)" : "";
            string label = $"Display {i + 1}: {s.Bounds.Width}×{s.Bounds.Height}{primaryTag}";

            list.Add(new MonitorOption
            {
                DeviceName = s.DeviceName,
                DisplayLabel = label,
                IsPrimary = s.Primary,
                Screen = s
            });
        }

        return list;
    }

    public static MonitorScreen? GetTargetScreen(string? preferredDeviceName = null)
    {
        var screens = EnumerateScreens();
        if (screens.Count == 0) return null;

        if (!string.IsNullOrEmpty(preferredDeviceName))
        {
            var match = screens.FirstOrDefault(s => s.DeviceName.Equals(preferredDeviceName, StringComparison.OrdinalIgnoreCase));
            if (match != null && match.Bounds.Width > 0 && match.Bounds.Height > 0)
                return match;
        }

        var fallback = screens.FirstOrDefault(s => s.Primary) ?? screens[0];
        return (fallback.Bounds.Width > 0 && fallback.Bounds.Height > 0) ? fallback : null;
    }

    /// <summary>Gets the effective DPI scale (1.0 = 96 DPI) for the destination <paramref name="screen"/>.</summary>
    public static (double DpiX, double DpiY) GetDpiScale(MonitorScreen? screen, Visual? fallbackVisual = null)
    {
        if (screen != null)
        {
            try
            {
                IntPtr hMon = screen.Handle;
                if (hMon == IntPtr.Zero)
                {
                    var rc = new RECT { Left = screen.Bounds.Left, Top = screen.Bounds.Top, Right = screen.Bounds.Right, Bottom = screen.Bounds.Bottom };
                    hMon = MonitorFromRect(ref rc, MONITOR_DEFAULTTONEAREST);
                }
                if (hMon != IntPtr.Zero && GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dx, out uint dy) == 0 && dx > 0 && dy > 0)
                    return (dx / 96.0, dy / 96.0);
            }
            catch { }
        }
        return GetDpiScale(fallbackVisual);
    }

    public static (double DpiX, double DpiY) GetDpiScale(Visual? visual)
    {
        if (visual != null)
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(visual);
                return (dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0, dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0);
            }
            catch { }
        }
        return (1.0, 1.0);
    }

    /// <summary>Rounds a DIP coordinate to a whole device pixel so window edges and the text inside never land on fractional pixels.</summary>
    public static double RoundToDevicePixel(double dip, double dpiScale) =>
        dpiScale > 0 ? Math.Round(dip * dpiScale) / dpiScale : dip;

    public static void PositionAtTopCenter(Window window, MonitorScreen screen, double topMargin = 4)
    {
        var (dpiX, dpiY) = GetDpiScale(screen, window);

        var bounds = screen.WorkingArea;
        double screenLeftDip = bounds.Left / dpiX;
        double screenWidthDip = bounds.Width / dpiX;
        double screenTopDip = bounds.Top / dpiY;

        double screenCenter = screenLeftDip + (screenWidthDip / 2.0);
        double left = screenCenter - (window.Width / 2.0);
        double top = screenTopDip + topMargin;

        window.Left = RoundToDevicePixel(left, dpiX);
        window.Top = RoundToDevicePixel(top, dpiY);
    }

    public static double GetTopY(Visual? visual, MonitorScreen screen, double topMargin = 4)
    {
        var (_, dpiY) = GetDpiScale(screen, visual);
        return (screen.WorkingArea.Top / dpiY) + topMargin;
    }

    public static double GetTopY(MonitorScreen screen, double topMargin = 4) => GetTopY(null, screen, topMargin);

    public static double GetBottomY(Visual? visual, MonitorScreen screen, double bottomMargin = 16, double pillHeight = 48)
    {
        var (_, dpiY) = GetDpiScale(screen, visual);
        return (screen.WorkingArea.Bottom / dpiY) - pillHeight - bottomMargin;
    }

    public static double GetBottomY(MonitorScreen screen, double bottomMargin = 16, double pillHeight = 48) => GetBottomY(null, screen, bottomMargin, pillHeight);
}
