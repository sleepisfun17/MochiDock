using System.Runtime.InteropServices;
using System.Text;

namespace MochiDock.Services;

public static class FullscreenPolicy
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr h, out Rect r);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    public static bool IsFullscreen(IntPtr own, string? targetMonitor = null)
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || h == own || h == GetShellWindow()) return false;

        var cls = new StringBuilder(64);
        if (GetClassName(h, cls, cls.Capacity) > 0)
        {
            string name = cls.ToString();
            if (name is "WorkerW" or "Progman" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return false;
        }

        if (!GetWindowRect(h, out var r)) return false;

        var fgScreen = System.Windows.Forms.Screen.FromHandle(h);
        var dockScreen = MultiMonitorHelper.GetTargetScreen(targetMonitor);
        if (dockScreen != null && !string.Equals(fgScreen.DeviceName, dockScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
            return false;

        var bounds = fgScreen.Bounds;
        return r.Left <= bounds.Left && r.Top <= bounds.Top && r.Right >= bounds.Right && r.Bottom >= bounds.Bottom;
    }
}