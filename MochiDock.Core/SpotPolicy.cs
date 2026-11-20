using System;

namespace MochiDock.Core;

public static class SpotPolicy
{
    public const double DragSlopDip = 6.0;
    public const double CenterSnapDip = 16.0;

    public static double ComputeWindowLeftDip(double screenLeftDip, double screenWidthDip, double windowWidthDip, double? horizontalRatio)
    {
        if (windowWidthDip >= screenWidthDip)
        {
            return screenLeftDip + (screenWidthDip - windowWidthDip) / 2.0;
        }

        double centerDip;
        if (horizontalRatio.HasValue && double.IsFinite(horizontalRatio.Value))
        {
            double ratio = Math.Clamp(horizontalRatio.Value, 0.0, 1.0);
            centerDip = screenLeftDip + ratio * screenWidthDip;
        }
        else
        {
            centerDip = screenLeftDip + screenWidthDip / 2.0;
        }

        double left = centerDip - windowWidthDip / 2.0;
        return Math.Clamp(left, screenLeftDip, screenLeftDip + screenWidthDip - windowWidthDip);
    }

    public static double CalculateRatio(double screenLeftDip, double screenWidthDip, double windowWidthDip, double currentLeftDip)
    {
        if (screenWidthDip <= 0) return 0.5;
        double centerDip = currentLeftDip + windowWidthDip / 2.0;
        return (centerDip - screenLeftDip) / screenWidthDip;
    }

    public static double? RememberRatio(double centerDip, double screenWidthDip)
    {
        if (double.IsNaN(screenWidthDip) || screenWidthDip <= 0.0 || !double.IsFinite(centerDip))
        {
            return null;
        }

        double mid = screenWidthDip / 2.0;
        if (Math.Abs(centerDip - mid) < CenterSnapDip)
        {
            return null; // Snapped to center
        }

        return Math.Clamp(centerDip / screenWidthDip, 0.0, 1.0);
    }

    public static double? RememberRatio(double ratio)
    {
        if (!double.IsFinite(ratio)) return null;
        if (Math.Abs(ratio - 0.5) < 0.02)
        {
            return null;
        }
        return Math.Clamp(ratio, 0.0, 1.0);
    }
}

