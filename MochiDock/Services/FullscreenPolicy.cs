using System.Runtime.InteropServices;
namespace MochiDock.Services;
public static class FullscreenPolicy {
 [StructLayout(LayoutKind.Sequential)] struct Rect{public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
 public static bool IsFullscreen(IntPtr own){var h=GetForegroundWindow();if(h==IntPtr.Zero||h==own||h==GetShellWindow()||!GetWindowRect(h,out var r))return false;var bounds=System.Windows.Forms.Screen.FromHandle(h).Bounds;return r.Left<=bounds.Left&&r.Top<=bounds.Top&&r.Right>=bounds.Right&&r.Bottom>=bounds.Bottom;}
}