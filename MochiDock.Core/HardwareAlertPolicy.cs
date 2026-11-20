namespace MochiDock.Core;

public sealed record AlertReading(double Value, string Source, DateTimeOffset CapturedAt, bool Measured = true);
public sealed class HardwareAlertPolicy
{
    sealed class Episode { public DateTimeOffset? Since, Last; public bool Armed = true; }
    readonly Dictionary<string, Episode> episodes = new();
    DateTimeOffset? lastNotice;
    public void ResetContinuity() { foreach (var e in episodes.Values) { e.Since = null; e.Last = null; } }
    public bool Evaluate(string metric, AlertReading? reading, double threshold, DateTimeOffset now)
    {
        if (!episodes.TryGetValue(metric, out var e)) episodes[metric] = e = new();
        if (reading is null || !reading.Measured || string.IsNullOrWhiteSpace(reading.Source) || !double.IsFinite(reading.Value) || reading.CapturedAt > now || now - reading.CapturedAt > TimeSpan.FromSeconds(5)) { e.Since = null; return false; }
        if (reading.Value < threshold) { e.Armed = true; e.Since = null; e.Last = reading.CapturedAt; return false; }
        if (e.Last == reading.CapturedAt) return false;
        if (e.Last is {} previous && reading.CapturedAt - previous > TimeSpan.FromSeconds(5)) e.Since = null;
        e.Last = reading.CapturedAt;
        e.Since ??= reading.CapturedAt;
        if (!e.Armed || reading.CapturedAt - e.Since < TimeSpan.FromSeconds(5)) return false;
        e.Armed = false;
        if (lastNotice is {} sent && now - sent < TimeSpan.FromMinutes(5)) return false;
        lastNotice = now;
        return true;
    }
}

public sealed class ExpansionTransition
{
    public bool Desired { get; private set; }
    public bool Running { get; private set; }
    public long Generation { get; private set; }
    public bool Request(bool expanded) { Desired = expanded; if (Running) return false; Running = true; Generation++; return true; }
    public bool Complete(long generation) { if (generation != Generation || !Running) return false; Running = false; return true; }
    public void Invalidate() { Generation++; Running = false; }
}
