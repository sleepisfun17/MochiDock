namespace MochiDock.Core;
public sealed class PrayerReminderGate
{
    private DateTimeOffset? _previous;
    private readonly HashSet<string> _sent = new();
    public void Reset(DateTimeOffset now) => _previous = now;
    public bool Due(string key, DateTimeOffset at, DateTimeOffset now)
    {
        bool due = _previous.HasValue && now >= _previous && now-_previous <= TimeSpan.FromSeconds(60) && at > _previous && at <= now && now-at <= TimeSpan.FromSeconds(60);
        return due && _sent.Add(key);
    }
    public void Advance(DateTimeOffset now) { _previous=now; if(_sent.Count>512)_sent.Clear(); }
}
public static class PrayerTime
{
    public static DateTimeOffset At(DateTime local, TimeZoneInfo zone)
    {
        local=DateTime.SpecifyKind(local,DateTimeKind.Unspecified);
        if(zone.IsInvalidTime(local)) throw new ArgumentException("Prayer time falls inside a daylight-saving gap.");
        return new DateTimeOffset(local,zone.GetUtcOffset(local));
    }
    public static string NormalizeSound(string mode) => mode == "Tone" ? "Tone" : "Silent";
    public static string MigrateSound(bool enabled,string mode) => !enabled ? "Silent" : NormalizeSound(mode);
}
