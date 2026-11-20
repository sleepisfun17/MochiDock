namespace MochiDock.Core;

public sealed class PrayerCountdownState
{
    string? acknowledged;
    string? current;
    public bool Evaluate(string identity, DateTimeOffset at, DateTimeOffset now)
    {
        if (current != identity) { current = identity; acknowledged = null; }
        return at > now && at - now <= TimeSpan.FromMinutes(10) && acknowledged != identity;
    }
    public void Acknowledge() => acknowledged = current;
    public static int Minutes(DateTimeOffset at, DateTimeOffset now) => Math.Max(0, (int)Math.Ceiling((at - now).TotalMinutes));
}
