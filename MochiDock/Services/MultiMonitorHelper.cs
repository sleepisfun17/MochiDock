using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;

namespace MochiDock.Services;

public class MonitorOption
{
    public string DeviceName { get; set; } = "";
    public string DisplayLabel { get; set; } = "";
    public bool IsPrimary { get; set; }
    public Screen Screen { get; set; } = null!;
}

public static class MultiMonitorHelper
{
    public static List<MonitorOption> GetAllMonitors()
    {
        var list = new List<MonitorOption>();
        var screens = Screen.AllScreens;

        for (int i = 0; i < screens.Length; i++)
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

    public static Screen? GetTargetScreen(string? preferredDeviceName = null)
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0)
        {
            var primary = Screen.PrimaryScreen;
            return (primary != null && primary.Bounds.Width > 0 && primary.Bounds.Height > 0) ? primary : null;
        }

        if (!string.IsNullOrEmpty(preferredDeviceName))
        {
            var match = screens.FirstOrDefault(s => s.DeviceName.Equals(preferredDeviceName, StringComparison.OrdinalIgnoreCase));
            if (match != null && match.Bounds.Width > 0 && match.Bounds.Height > 0)
            {
                return match;
            }
        }

        var fallback = Screen.PrimaryScreen ?? screens[0];
        return (fallback != null && fallback.Bounds.Width > 0 && fallback.Bounds.Height > 0) ? fallback : null;
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

    public static void PositionAtTopCenter(Window window, Screen screen, double topMargin = 4)
    {
        var (dpiX, dpiY) = GetDpiScale(window);

        var bounds = screen.WorkingArea;
        double screenLeftDip = bounds.Left / dpiX;
        double screenWidthDip = bounds.Width / dpiX;
        double screenTopDip = bounds.Top / dpiY;

        double screenCenter = screenLeftDip + (screenWidthDip / 2.0);
        double left = screenCenter - (window.Width / 2.0);
        double top = screenTopDip + topMargin;

        window.Left = left;
        window.Top = top;
    }

    public static double GetTopY(Visual? visual, Screen screen, double topMargin = 4)
    {
        var (_, dpiY) = GetDpiScale(visual);
        return (screen.WorkingArea.Top / dpiY) + topMargin;
    }

    public static double GetTopY(Screen screen, double topMargin = 4) => GetTopY(null, screen, topMargin);

    public static double GetBottomY(Visual? visual, Screen screen, double bottomMargin = 16, double pillHeight = 48)
    {
        var (_, dpiY) = GetDpiScale(visual);
        return (screen.WorkingArea.Bottom / dpiY) - pillHeight - bottomMargin;
    }

    public static double GetBottomY(Screen screen, double bottomMargin = 16, double pillHeight = 48) => GetBottomY(null, screen, bottomMargin, pillHeight);
}
