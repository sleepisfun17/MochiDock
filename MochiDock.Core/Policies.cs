namespace MochiDock.Core;

public sealed class RequestGeneration
{
    private long _generation;
    public long Next() => Interlocked.Increment(ref _generation);
    public bool IsCurrent(long generation) => Interlocked.Read(ref _generation) == generation;
}

public static class NotificationPolicy
{
    public static bool IsQuiet(TimeSpan now, TimeSpan start, TimeSpan end) =>
        start <= end ? now >= start && now < end : now >= start || now < end;
}

public static class PrayerGeometry
{
    public static double QiblaBearing(double latitude, double longitude)
    {
        const double radians = Math.PI / 180;
        double delta = (39.826206 - longitude) * radians;
        double lat = latitude * radians;
        double target = 21.422487 * radians;
        return (Math.Atan2(Math.Sin(delta), Math.Cos(lat) * Math.Tan(target) - Math.Sin(lat) * Math.Cos(delta)) / radians + 360) % 360;
    }
}
