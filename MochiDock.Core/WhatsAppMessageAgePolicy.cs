using System.Globalization;

namespace MochiDock.Core;

/// <summary>
/// Decides whether a message that arrived by webhook is new enough to notify about. A freshly linked or re-synced WhatsApp session
/// replays history as webhook events; without this every old message would pop up as a new notification.
/// Pure logic: no clock, I/O or WPF.
/// </summary>
public static class WhatsAppMessageAgePolicy
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);
    /// <summary>Right after (re)starting the service history replays are most likely, so be stricter.</summary>
    public static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan StartupMaxAge = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Reads a message time that is either ISO-8601 or Unix seconds / milliseconds (as text). Returns null when it cannot be understood.
    /// </summary>
    public static DateTimeOffset? ParseMessageTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            if (number <= 0 || double.IsNaN(number) || double.IsInfinity(number)) return null;
            try
            {
                // Values this large are milliseconds (seconds would be beyond year 5000).
                return number > 100_000_000_000d
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)number)
                    : DateTimeOffset.FromUnixTimeSeconds((long)number);
            }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }

    /// <param name="messageTime">When the message was sent, if the payload said so.</param>
    /// <param name="now">Current time.</param>
    /// <param name="sinceServiceStart">How long the WhatsApp service has been connected.</param>
    public static bool ShouldNotify(DateTimeOffset? messageTime, DateTimeOffset now, TimeSpan sinceServiceStart)
    {
        bool inGrace = sinceServiceStart < StartupGrace;
        // No usable time: normal messages go through, but right after start it is more likely a replay.
        if (messageTime is null) return !inGrace;

        var age = now - messageTime.Value;
        // A message "from the future" is clock skew between the server and this PC, not an old message.
        if (age < TimeSpan.Zero) return true;
        if (age > MaxAge) return false;
        return !inGrace || age <= StartupMaxAge;
    }
}
