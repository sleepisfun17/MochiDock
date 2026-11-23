namespace MochiDock.Core;

/// <summary>One status badge: the glyph to draw, the disc colour and the glyph colour. Hex strings, "#RRGGBB".</summary>
public readonly record struct BadgeSpec(string Glyph, string Fill, string Ink);

/// <summary>
/// The small status badge in the corner of the mascot (like a phone app badge). At the capsule size (about 22 x 19 px) the little props that
/// used to float above the head were 3 to 5 px thin lines, so every face whose meaning was such a prop now shows it as a glyph in one disc.
/// The colours say what the badge means, not which theme is on: red needs you, green is good or a pause, amber and gold are weather and
/// prizes, navy is night, teal is a reminder. This table is the source of truth for the faces; faces that are not listed have no badge.
/// </summary>
public static class MascotBadge
{
    public const string White = "#FFFFFF";
    public const string DarkRing = "#1B2318";   // the ring on the light Matcha ground (its dark ink #283322 darkened for 3:1 against the fills)

    private const string Red = "#D13438";
    private const string Green = "#1B7F46";
    private const string Navy = "#2F3E8F";

    private const string Purple = "#6E4BB8";
    private const string GameGreen = "#0F6E56";
    private const string Slate = "#3F4C66";

    /// <summary>
    /// The badge of a face for a mascot. The cats (Kuro and Shiro) wear no headset (it sits badly on ears), so the faces the headset told
    /// (Music, Gaming, Worried, In a meeting) get a badge of their own instead; the Mochi kinds keep the headset and have none for them.
    /// </summary>
    public static BadgeSpec? For(MascotMood mood, MascotKind kind)
    {
        if (kind is MascotKind.Kuro or MascotKind.Shiro)
        {
            switch (mood)
            {
                case MascotMood.Music: return new("note", Purple, White);
                case MascotMood.Focused or MascotMood.Worried: return new("controller", GameGreen, White);
                case MascotMood.Meeting: return new("mic", Slate, White);
            }
        }
        return For(mood);
    }

    /// <summary>The badge of a face for the Mochi kinds (the cats' extra badges are in <see cref="For(MascotMood, MascotKind)"/>).</summary>
    public static BadgeSpec? For(MascotMood mood) => mood switch
    {
        MascotMood.Alert or MascotMood.Excited => new("exclaim", Red, White),
        MascotMood.Sleeping => new("z", Navy, White),
        MascotMood.Yawning => new("moon", Navy, White),
        MascotMood.Coffee => new("cup", "#8A5A3C", White),
        MascotMood.Praying => new("bell", "#0E7C86", White),
        MascotMood.BreakTime => new("pause", Green, White),
        MascotMood.Victory => new("trophy", "#8F6B0E", White),
        MascotMood.Angry => new("clock", Red, White),
        MascotMood.Drained => new("battery", Red, White),
        MascotMood.Money => new("up", Green, White),
        MascotMood.MoneyDown => new("down", Red, White),
        MascotMood.Sunny => new("sun", "#A85F00", White),
        MascotMood.Rainy => new("umbrella", "#2B6FD6", White),
        MascotMood.Snowy => new("snow", "#2E6FA8", White),
        MascotMood.Cloudy => new("cloud", "#5B6B8C", White),
        MascotMood.Studying => new("timer", Red, White),
        MascotMood.Proud => new("tick", Green, White),
        _ => null,
    };

    /// <summary>The ring that separates the badge from the body: white on the dark grounds, dark on the light Matcha ground.</summary>
    public static string RingFor(string? theme) => ThemePolicy.Normalize(theme) == ThemePolicy.Matcha ? DarkRing : White;

    /// <summary>The theme's ring, or the other one when it would be under 3:1 against this disc colour (a navy disc on Matcha).</summary>
    public static string RingFor(string? theme, string fill)
    {
        string ring = RingFor(theme);
        if (Contrast(ring, fill) >= 3) return ring;
        string other = ring == White ? DarkRing : White;
        return Contrast(other, fill) > Contrast(ring, fill) ? other : ring;
    }

    /// <summary>WCAG contrast ratio of two "#RRGGBB" colours.</summary>
    public static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        string h = hex.TrimStart('#');
        int r = Convert.ToInt32(h.Substring(0, 2), 16), g = Convert.ToInt32(h.Substring(2, 2), 16), b = Convert.ToInt32(h.Substring(4, 2), 16);
        return MascotTint.Luminance(r, g, b);
    }
}
