namespace MochiDock.Core;

public static class SolarPosition
{
    // NOAA fractional-year approximation; calculated elevation, not an observed measurement.
    public static double Elevation(DateTimeOffset instant, double latitude, double longitude)
    {
        var utc = instant.UtcDateTime;
        double gamma = 2 * Math.PI / (DateTime.IsLeapYear(utc.Year) ? 366 : 365) * (utc.DayOfYear - 1 + (utc.TimeOfDay.TotalHours - 12) / 24);
        double equation = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma) - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
        double declination = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma) - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma) - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);
        double minutes = ((utc.TimeOfDay.TotalMinutes + equation + 4 * longitude) % 1440 + 1440) % 1440;
        double hourAngle = (minutes / 4 - 180) * Math.PI / 180;
        double lat = latitude * Math.PI / 180;
        return Math.Asin(Math.Clamp(Math.Sin(lat) * Math.Sin(declination) + Math.Cos(lat) * Math.Cos(declination) * Math.Cos(hourAngle), -1, 1)) * 180 / Math.PI;
    }
    public static bool ClickThrough(bool gaming, bool expanded, bool enableClickThrough = true) => enableClickThrough && gaming && !expanded;
}
