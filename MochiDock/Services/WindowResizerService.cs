using System.Runtime.InteropServices;
using System.Text;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>
/// Resizes the foreground window for the window-size shortcuts (it replaces the AutoHotkey WindowResizer script). The window is
/// restored if it is maximised, given the exact outer size in physical pixels, and centred on the work area of the monitor it is on
/// (<see cref="WindowSizing.Place"/>). MochiDock is per-monitor DPI aware, so the sizes are real pixels on every monitor.
/// It only calls public user32 APIs on the window; there is no keyboard hook and nothing is injected into the other process.
/// </summary>
public static class WindowResizerService
{
    public sealed record Result(bool Success, string Message);

    public static IntPtr CaptureForegroundWindow() => GetForegroundWindow();

    public static Result Resize(int width, int height, IntPtr ownWindow, IntPtr? targetWindow = null)
    {
        IntPtr hwnd = targetWindow ?? GetForegroundWindow();
        if (!IsResizable(hwnd, ownWindow, out string? why)) return new Result(false, why!);

        if (IsZoomed(hwnd) || IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return new Result(false, "Couldn't find the window's monitor");

        var (x, y) = WindowSizing.Place(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom, width, height);
        if (!SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER))
            return new Result(false, "This window can't be resized");

        // Some windows enforce their own minimum or aspect ratio; report what they actually became.
        if (GetWindowRect(hwnd, out var after))
        {
            int w = after.Right - after.Left, h = after.Bottom - after.Top;
            if (w != width || h != height) return new Result(true, $"Window {WindowSizing.FormatSize(w, h)} (asked {WindowSizing.FormatSize(width, height)})");
        }
        return new Result(true, $"Window {WindowSizing.FormatSize(width, height)}");
    }

    /// <summary>Swaps the foreground window's width and height (landscape to portrait and back).</summary>
    public static Result Flip(IntPtr ownWindow, IntPtr? targetWindow = null)
    {
        IntPtr hwnd = targetWindow ?? GetForegroundWindow();
        if (!IsResizable(hwnd, ownWindow, out string? why)) return new Result(false, why!);
        if (IsZoomed(hwnd) || IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        if (!GetWindowRect(hwnd, out var rect)) return new Result(false, "This window can't be resized");
        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        if (w < WindowSizing.MinSide || h < WindowSizing.MinSide) return new Result(false, "This window can't be resized");
        return Resize(h, w, ownWindow, hwnd);
    }

    static bool IsResizable(IntPtr hwnd, IntPtr ownWindow, out string? why)
    {
        why = null;
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) { why = "No active window to resize"; return false; }
        if (hwnd == ownWindow || GetAncestor(hwnd, GA_ROOT) == ownWindow) { why = "Click the window you want to resize first"; return false; }
        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        // The desktop and the taskbar are not windows to resize.
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            why = "Click the window you want to resize first";
            return false;
        }
        return true;
    }

    const int SW_RESTORE = 9;
    const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;
    const uint MONITOR_DEFAULTTONEAREST = 2;
    const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll", SetLastError = true)] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
