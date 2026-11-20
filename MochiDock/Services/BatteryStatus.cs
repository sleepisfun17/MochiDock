using System.Runtime.InteropServices;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>Reads Windows' system power status (AC line and battery level).</summary>
public static class BatteryStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>The current state, or null when Windows reports none. HasBattery is false on desktops or for an unknown level.</summary>
    public static MochiDock.Core.PowerState? Read()
    {
        if (!GetSystemPowerStatus(out var s)) return null;
        // BatteryFlag 128 = no system battery, 255 = unknown; the level is 255 when unknown.
        bool hasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255 && s.BatteryLifePercent <= 100;
        return new MochiDock.Core.PowerState(hasBattery, s.ACLineStatus == 1, hasBattery ? s.BatteryLifePercent : 0);
    }
}
