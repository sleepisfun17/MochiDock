namespace MochiDock.Core;

public static class PrayerTimeline
{
    public static readonly IReadOnlyList<double> Slots = Array.AsReadOnly(new[] { .1,.3,.5,.7,.9 });
    public static double? Position(DateTimeOffset now,DateTimeOffset start,DateTimeOffset end,IReadOnlyList<DateTimeOffset> prayers)
    {
        if(prayers.Count!=5||now<start||now>=end||end<=start)return null;
        DateTimeOffset[] times=[start,..prayers,end];
        double[] positions=[0,.1,.3,.5,.7,.9,1];
        for(int i=1;i<times.Length;i++)if(times[i]<=times[i-1])return null;
        for(int i=1;i<times.Length;i++)
            if(now<=times[i])return positions[i-1]+(now-times[i-1]).TotalSeconds/(times[i]-times[i-1]).TotalSeconds*(positions[i]-positions[i-1]);
        return null;
    }
    public static double Height(double position)
    {
        double p=Math.Clamp(position,0,1);
        if(p<=.3)return 12+36*Math.Pow(1-p/.3,1.7);
        if(p<=.7)return 12+26*.5*(1-Math.Cos(Math.PI*(p-.3)/.4));
        return 38+10*Math.Sin(Math.PI*.5*(p-.7)/.3);
    }
    // Radii: ring 9.5, moving halo 9, sun body 5, target dot 3; leave a two-DIP gap.
    public static int TargetSize(double distance) => distance>=20.5 ? 19 : distance>=10 ? 6 : 0;

    public static bool IsNextSlot(int slotIndex, DateTimeOffset? nextPrayerInstant, DateTimeOffset nowUtc, IReadOnlyList<DateTimeOffset> todayInstants)
    {
        if (slotIndex < 0 || slotIndex >= todayInstants.Count || !nextPrayerInstant.HasValue) return false;
        if (nextPrayerInstant.Value == todayInstants[slotIndex]) return true;
        // After today's Isha has passed (or after the final prayer instant), next prayer rolls over to tomorrow's Fajr (slot 0)
        if (slotIndex == 0 && todayInstants.Count > 0 && nowUtc >= todayInstants[^1] && nextPrayerInstant.Value > todayInstants[^1])
            return true;
        return false;
    }

    public static int GetCurrentSlotIndex(DateTimeOffset nowUtc, IReadOnlyList<DateTimeOffset> todayInstants)
    {
        if (todayInstants == null || todayInstants.Count != 5) return -1;
        if (nowUtc < todayInstants[0]) return 4; // Before Fajr, yesterday's Isha is ongoing
        for (int i = 4; i >= 0; i--)
        {
            if (nowUtc >= todayInstants[i]) return i;
        }
        return -1;
    }
}
