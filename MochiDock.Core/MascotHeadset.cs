namespace MochiDock.Core;

/// <summary>The colours of the headset of one mascot: the band and the cups, the band's rim and the cups' ring. Hex strings, "#RRGGBB".</summary>
public readonly record struct HeadsetLook(string Band, string BandRim, string Cup, string CupRing);

/// <summary>
/// The headset (Music, Gaming, Worried and Meeting faces) matches the mascot instead of being bright blue or red. The rule: the mascot's own
/// palette decides. Grey Mochi wears graphite with a silver rim, Matcha Mochi deep matcha with foam, Hojicha Mochi roast with caramel, Kuro
/// charcoal with electric yellow and Shiro white with an ice-blue ring, on every theme. Two exceptions come from the theme's ground: grey
/// Mochi on Kuro Night wears storm with electric yellow (as he always did there), and Shiro's band rim is darker on the light Matcha ground.
/// The face is shown by marks (red lights, a microphone), not by the colour.
/// </summary>
public static class MascotHeadset
{
    public const string GamingLed = "#E5484D";

    public static HeadsetLook For(MascotKind kind, string? theme)
    {
        string t = ThemePolicy.Normalize(theme);
        return kind switch
        {
            MascotKind.Kuro => new("#2A2C3D", "#FFD43B", "#2A2C3D", "#FFD43B"),
            MascotKind.Shiro => new("#F7F7FA", t == ThemePolicy.Matcha ? MascotRim.DarkNeutral : "#B8B9C0", "#F7F7FA", "#5AA9E6"),
            MascotKind.HojichaMochi => new("#5A4030", "#E0A96D", "#5A4030", "#E0A96D"),
            MascotKind.MatchaMochi => new("#365D2A", "#F5F0DF", "#365D2A", "#F5F0DF"),
            _ => t == ThemePolicy.KuroNight
                ? new("#2A2C3D", "#FFD43B", "#2A2C3D", "#FFD43B")
                : new("#3A3D4A", "#EBEBF0", "#3A3D4A", "#EBEBF0"),
        };
    }
}
