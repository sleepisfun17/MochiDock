namespace MochiDock.Core;

/// <summary>
/// Who is drawn: Mochi (the grey round mascot), Matcha Mochi (green), Hojicha Mochi (tan), Kuro (the black electric cat) or Shiro (the white cat).
/// The mascot decides the body colours; the theme only sets the ground and the UI colours.
/// </summary>
public enum MascotKind { Mochi, MatchaMochi, HojichaMochi, Kuro, Shiro }

/// <summary>The Mascot setting: Auto, Mochi, MatchaMochi, HojichaMochi, Kuro or Shiro. Unknown names fall back to Auto.</summary>
public static class MascotChoice
{
    public const string Auto = "Auto";
    public const string Mochi = "Mochi";
    public const string MatchaMochi = "MatchaMochi";
    public const string HojichaMochi = "HojichaMochi";
    public const string Kuro = "Kuro";
    public const string Shiro = "Shiro";

    public static string Normalize(string? choice)
    {
        string c = (choice ?? "").Replace(" ", "");
        foreach (var name in new[] { Mochi, MatchaMochi, HojichaMochi, Kuro, Shiro })
            if (string.Equals(c, name, StringComparison.OrdinalIgnoreCase)) return name;
        return Auto;
    }

    /// <summary>
    /// Auto is the theme's own mascot: grey Mochi on Default, Hojicha Mochi on Hojicha, Matcha Mochi on Matcha and Kuro on Kuro Night.
    /// Any other choice is that mascot on every theme.
    /// </summary>
    public static MascotKind Resolve(string? choice, string? theme) => Normalize(choice) switch
    {
        Mochi => MascotKind.Mochi,
        MatchaMochi => MascotKind.MatchaMochi,
        HojichaMochi => MascotKind.HojichaMochi,
        Kuro => MascotKind.Kuro,
        Shiro => MascotKind.Shiro,
        _ => ThemePolicy.Normalize(theme) switch
        {
            ThemePolicy.KuroNight => MascotKind.Kuro,
            ThemePolicy.Matcha => MascotKind.MatchaMochi,
            ThemePolicy.Hojicha => MascotKind.HojichaMochi,
            _ => MascotKind.Mochi,
        },
    };

    /// <summary>True for the three round Mochi colours (as opposed to the cats).</summary>
    public static bool IsMochi(MascotKind kind) => kind is MascotKind.Mochi or MascotKind.MatchaMochi or MascotKind.HojichaMochi;
}
