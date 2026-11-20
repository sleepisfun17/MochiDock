using MochiDock.Models;
namespace MochiDock.Services;

// Approximate offline solar fallback. API schedules are authoritative when available.
internal sealed class LegacyPrayerCalculator
{
    private double _latitude, _longitude;
    private string _cachedCity="", _resolvedLocationName="", _hijriDate="";
    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;
    public void SetCoordinates(double latitude,double longitude,string city,string name)
    { _latitude=latitude;_longitude=longitude;_cachedCity=city;_resolvedLocationName=name; }
    public PrayerSchedule CalculateSchedule(DateTime date, string method = "MWL")
    {
        double fajrAngle = 18.0;
        double ishaAngle = 17.0;
        bool ishaInterval = false;

        switch (method)
        {
            case "MWL":
                fajrAngle = 18.0;
                ishaAngle = 17.0;
                break;
            case "ISNA":
                fajrAngle = 15.0;
                ishaAngle = 15.0;
                break;
            case "Egypt":
                fajrAngle = 19.5;
                ishaAngle = 17.5;
                break;
            case "UmmAlQura":
                fajrAngle = 18.5;
                ishaInterval = true;
                break;
            case "Karachi":
                fajrAngle = 18.0;
                ishaAngle = 18.0;
                break;
            case "Kemenag":
                fajrAngle = 20.0;
                ishaAngle = 18.0;
                break;
            default: // Default to MWL for global / Japan accuracy
                fajrAngle = 18.0;
                ishaAngle = 17.0;
                break;
        }

        var schedule = new PrayerSchedule
        {
            City = _cachedCity,
            ResolvedLocation = _resolvedLocationName,
            HijriDate = _hijriDate,
            Date = date
        };

        // Astronomical calculations
        double timeZone = Zone.GetUtcOffset(date).TotalHours;
        int dayOfYear = date.DayOfYear;

        // Equation of time & solar declination
        double b = 2.0 * Math.PI * (dayOfYear - 81) / 365.0;
        double eot = 9.87 * Math.Sin(2 * b) - 7.53 * Math.Cos(b) - 1.5 * Math.Sin(b); // minutes
        double declination = 23.45 * Math.Sin((360.0 / 365.0) * (dayOfYear - 81) * Math.PI / 180.0); // degrees
        double declinationRad = declination * Math.PI / 180.0;
        double latRad = _latitude * Math.PI / 180.0;

        // Solar Noon (Zero artificial offsets for exact alignment with Awqat Salaat)
        double noonHours = 12.0 + timeZone - (_longitude / 15.0) - (eot / 60.0);

        // Sunrise & Sunset angle (accounting for refraction 0.833°)
        double sunAngle = -0.833;
        double sunHourAngle = GetHourAngle(latRad, declinationRad, sunAngle);

        // Fajr angle
        double fajrHourAngle = GetHourAngle(latRad, declinationRad, -fajrAngle);

        // Isha angle
        double ishaHourAngle = ishaInterval ? 0 : GetHourAngle(latRad, declinationRad, -ishaAngle);

        // Asr angle: Standard Shafi'i / Maliki / Hanbali
        double asrAltitude = Math.Atan(1.0 / (1.0 + Math.Tan(Math.Abs(latRad - declinationRad)))) * 180.0 / Math.PI;
        double asrHourAngle = GetHourAngle(latRad, declinationRad, asrAltitude);

        // Convert hour angles to local times (No arbitrary +2m offsets)
        double fajrHours = noonHours - (fajrHourAngle / 15.0);
        double sunriseHours = noonHours - (sunHourAngle / 15.0);
        double dhuhrHours = noonHours;
        double asrHours = noonHours + (asrHourAngle / 15.0);
        double maghribHours = noonHours + (sunHourAngle / 15.0);
        double ishaHours = ishaInterval ? maghribHours + 1.5 : noonHours + (ishaHourAngle / 15.0);
        double imsakHours = fajrHours - (10.0 / 60.0); // 10 min before Fajr

        schedule.Imsak.Time = HoursToTimeSpan(imsakHours);
        schedule.Fajr.Time = HoursToTimeSpan(fajrHours);
        schedule.Sunrise.Time = HoursToTimeSpan(sunriseHours);
        schedule.Dhuhr.Time = HoursToTimeSpan(dhuhrHours);
        schedule.Asr.Time = HoursToTimeSpan(asrHours);
        schedule.Maghrib.Time = HoursToTimeSpan(maghribHours);
        schedule.Isha.Time = HoursToTimeSpan(ishaHours);

        EvaluateScheduleStatus(schedule);
        return schedule;
    }

    private static void EvaluateScheduleStatus(PrayerSchedule schedule)
    {
        schedule.QiblaBearingText=$"Qibla {MochiDock.Core.PrayerGeometry.QiblaBearing(schedule.Latitude,schedule.Longitude):0}°";
    }
    private static double GetHourAngle(double latRad, double decRad, double altitudeDeg)
    {
        double altRad = altitudeDeg * Math.PI / 180.0;
        double cosHA = (Math.Sin(altRad) - Math.Sin(latRad) * Math.Sin(decRad)) / (Math.Cos(latRad) * Math.Cos(decRad));
        if (cosHA > 1.0 || cosHA < -1.0) throw new InvalidOperationException("Offline calculation unavailable at this latitude/date.");
        return Math.Acos(cosHA) * 180.0 / Math.PI;
    }

    private static TimeSpan HoursToTimeSpan(double hours)
    {
        while (hours < 0) hours += 24.0;
        while (hours >= 24) hours -= 24.0;
        int h = (int)hours;
        double remainMin = (hours - h) * 60.0;
        int m = (int)remainMin;
        int s = (int)Math.Round((remainMin - m) * 60.0);
        if (s >= 60)
        {
            s = 0;
            m++;
            if (m >= 60)
            {
                m = 0;
                h = (h + 1) % 24;
            }
        }
        return new TimeSpan(h, m, s);
    }
}
