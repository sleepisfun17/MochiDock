using System;
using System.Runtime.InteropServices;

namespace MochiDock.Services;

/// <summary>
/// How long the user has not touched the keyboard or mouse, from the system's own last-input time. A plain query: nothing is hooked
/// and nothing about the input itself is read.
/// </summary>
public static class UserIdleService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    /// <summary>Time since the last input, or zero when the system cannot say (so "idle" is never assumed).</summary>
    public static TimeSpan IdleTime()
    {
        try
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            // Both values are 32-bit tick counts that wrap together, so the unsigned difference is right across a wrap.
            uint elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
            return TimeSpan.FromMilliseconds(elapsed);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Idle", ex);
            return TimeSpan.Zero;
        }
    }
}
