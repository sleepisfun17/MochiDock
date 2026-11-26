namespace MochiDock.Core;

/// <summary>One status badge: the glyph to draw, the disc colour and the glyph colour. Hex strings, "#RRGGBB".</summary>
public readonly record struct BadgeSpec(string Glyph, string Fill, string Ink);

/// <summary>Three-stop dome gradient stops for the 3D charm: lighter top highlight, base mid and deeper bottom shade.</summary>
public readonly record struct BadgeShades(string Top, string Mid, string Bottom);

/// <summary>Position and scale of the status charm in the mascot's 30 x 26 canvas.</summary>
public readonly record struct BadgePlacement(double X, double Y, double Scale);

/// <summary>Visual style of the mascot's status badge.</summary>
public enum MascotBadgeStyle
{
    /// <summary>3D glossy squircle charm.</summary>
    Charm,
    /// <summary>3D glossy speech bubble with a coloured glyph.</summary>
    Bubble,
    /// <summary>3D die-cut silhouette sticker with a thick ring outline.</summary>
    Sticker,
    /// <summary>No status badge (eyes and face expressions only).</summary>
    Off,
}

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
    /// The badge of a face for a mascot. The cats (Kuro and Shiro) wear no headset (it sits badly on ears), so Gaming, Worried and In a
    /// meeting get a badge of their own instead; their Music face shows a floating note beside the head (<see cref="MascotNote"/>), because a
    /// note in a 6 px disc did not read as music. The Mochi kinds keep the headset and have none of these.
    /// </summary>
    public static BadgeSpec? For(MascotMood mood, MascotKind kind)
    {
        if (kind is MascotKind.Kuro or MascotKind.Shiro)
        {
            switch (mood)
            {
                case MascotMood.Music: return null;
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
        MascotMood.Workout => new("dumbbell", GameGreen, White),
        MascotMood.Eating => new("utensils", "#B84D14", White),
        MascotMood.Working => new("briefcase", Slate, White),
        MascotMood.Proud => new("tick", Green, White),
        _ => null,
    };

    public static MascotBadgeStyle ParseStyle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return MascotBadgeStyle.Charm;
        return value.Trim().ToLowerInvariant() switch
        {
            "bubble" => MascotBadgeStyle.Bubble,
            "sticker" => MascotBadgeStyle.Sticker,
            "off" or "none" => MascotBadgeStyle.Off,
            _ => MascotBadgeStyle.Charm,
        };
    }

    public static string NormalizeStyle(string? value) => ParseStyle(value).ToString();

    /// <summary>
    /// Position of the status badge in the mascot's 30 x 26 canvas: Mochi kinds sit at the top-right shoulder (anchored slightly outward to
    /// clear the right eye); the cats (Kuro and Shiro) sit at the bottom-right cheek/shoulder so both ears stay clear and the left tail is balanced.
    /// </summary>
    public static BadgePlacement PlacementFor(MascotKind kind) => PlacementFor(kind, MascotBadgeStyle.Charm);

    public static BadgePlacement PlacementFor(MascotKind kind, MascotBadgeStyle style)
    {
        bool isCat = kind is MascotKind.Kuro or MascotKind.Shiro;
        return style switch
        {
            MascotBadgeStyle.Bubble => isCat ? new BadgePlacement(28.2, 21.5, 1.0) : new BadgePlacement(28.2, 1.8, 1.0),
            MascotBadgeStyle.Sticker => isCat ? new BadgePlacement(28.2, 21.2, 1.24) : new BadgePlacement(27.8, 2.2, 1.24),
            _ => isCat ? new BadgePlacement(28.0, 21.5, 1.0) : new BadgePlacement(27.8, 2.2, 1.0),
        };
    }

    /// <summary>
    /// Computes top highlight (+16% toward white), mid (normalized base) and bottom shade (-26% toward black) "#RRGGBB" stops for the
    /// 3D dome gradient while keeping semantic colours recognizable.
    /// </summary>
    public static BadgeShades ShadePair(string fillHex)
    {
        var (r, g, b) = ParseRgb(fillHex);
        string top = FormatHex(
            Math.Clamp((int)Math.Round(r + (255 - r) * 0.16), 0, 255),
            Math.Clamp((int)Math.Round(g + (255 - g) * 0.16), 0, 255),
            Math.Clamp((int)Math.Round(b + (255 - b) * 0.16), 0, 255));
        string mid = FormatHex(r, g, b);
        string bottom = FormatHex(
            Math.Clamp((int)Math.Round(r * 0.74), 0, 255),
            Math.Clamp((int)Math.Round(g * 0.74), 0, 255),
            Math.Clamp((int)Math.Round(b * 0.74), 0, 255));
        return new BadgeShades(top, mid, bottom);
    }

    private static (int R, int G, int B) ParseRgb(string hex)
    {
        string h = hex.Trim().TrimStart('#');
        return (
            Convert.ToInt32(h.Substring(0, 2), 16),
            Convert.ToInt32(h.Substring(2, 2), 16),
            Convert.ToInt32(h.Substring(4, 2), 16));
    }

    private static string FormatHex(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";

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
        var (r, g, b) = ParseRgb(hex);
        return MascotTint.Luminance(r, g, b);
    }

    /// <summary>Human-readable tooltip describing the mascot and its current mood/badge.</summary>
    public static string DescribeMood(MascotMood mood, MascotKind kind)
    {
        string name = kind switch
        {
            MascotKind.Kuro => "Kuro",
            MascotKind.Shiro => "Shiro",
            MascotKind.MatchaMochi => "Matcha Mochi",
            MascotKind.HojichaMochi => "Hojicha Mochi",
            _ => "Mochi",
        };
        string state = mood switch
        {
            MascotMood.Alert => "Notice",
            MascotMood.Excited => "New message",
            MascotMood.Sleeping => "Sleeping",
            MascotMood.Yawning => "Up late",
            MascotMood.Coffee => "Coffee break",
            MascotMood.Praying => "Prayer time",
            MascotMood.BreakTime => "Break time",
            MascotMood.Victory => "Victory!",
            MascotMood.Angry => "Overdue task",
            MascotMood.Drained => "Low battery",
            MascotMood.Money => "FX up",
            MascotMood.MoneyDown => "FX down",
            MascotMood.Sunny => "Sunny",
            MascotMood.Rainy => "Rainy",
            MascotMood.Snowy => "Snowy",
            MascotMood.Cloudy => "Cloudy",
            MascotMood.Studying => "Studying",
            MascotMood.Proud => "All tasks done",
            MascotMood.Music => "Listening to music",
            MascotMood.Focused => "Gaming",
            MascotMood.Worried => "Low FPS",
            MascotMood.Meeting => "In a meeting",
            MascotMood.Workout => "Working out",
            MascotMood.Eating => "Eating",
            MascotMood.Working => "Working",
            MascotMood.Happy => "Happy",
            MascotMood.Hot => "Running hot",
            MascotMood.Freezing => "Freezing",
            MascotMood.Dizzy => "High CPU / RAM",
            MascotMood.Defeat => "Tough match",
            MascotMood.Surprised => "Surprised",
            MascotMood.Cheering => "Cheering",
            MascotMood.Bored => "Bored",
            MascotMood.Waking => "Waking up",
            MascotMood.Electric => "Battery & power",
            MascotMood.Birthday => "Happy birthday!",
            MascotMood.Waiting => "Thinking",
            _ => "Chilling",
        };
        return $"{name} · {state}";
    }
}
