namespace MochiDock.Core;

/// <summary>
/// The rim of every mascot but Kuro on the Matcha theme: its ground is a light oat colour, so the thin light rims of grey Mochi, Hojicha
/// Mochi and Shiro all but vanish at the capsule size, and even the green Matcha Mochi's coloured faces (Electric, Freezing, Angry) fall
/// under 3:1 there. On that ground the rim is darker and thicker.
/// Kuro (he has a glow) and every dark ground are unchanged.
/// </summary>
public static class MascotRim
{
    public const string DarkNeutral = "#70727E";
    public const double NormalThickness = 0.8;
    public const double LightGroundThickness = 1.5;
    /// <summary>The cats' shared outline on Matcha: started at 1.1 and raised (1.2, 1.3, 1.4) until the 22 x 19 visibility check (3:1 at 100%) passed, which took 1.5; always on body, ears and tail together.</summary>
    public const double CatLightGroundThickness = 1.5;
    /// <summary>The cats' outline on the dark grounds. 1.0, not 0.8: WPF layout rounding used to snap the Mochi border 0.8 to a whole unit, and the cats' exact border at 0.8 fell under 3:1 for the coloured faces.</summary>
    public const double CatNormalThickness = 1.0;
    /// <summary>The darkest-needed relative luminance of a rim on the light ground (about 3.8:1 against Matcha's #EFE6CF).</summary>
    public const double MaxLuminance = 0.17;

    public static bool NeedsDarkRim(MascotKind kind, string? theme) =>
        IsLightGround(theme) && kind is not MascotKind.Kuro;

    /// <summary>True for the light Matcha ground, where a glow has nothing dark to shine against (Kuro drops his there; see <see cref="MascotTint.OnLightGround"/>).</summary>
    public static bool IsLightGround(string? theme) => ThemePolicy.Normalize(theme) == ThemePolicy.Matcha;

    /// <summary>The Mochi kinds: 1.5 on Matcha, 0.8 elsewhere. The cats: <see cref="CatThickness"/>.</summary>
    public static double Thickness(MascotKind kind, string? theme) =>
        kind is MascotKind.Kuro or MascotKind.Shiro ? CatThickness(kind, theme)
        : NeedsDarkRim(kind, theme) ? LightGroundThickness : NormalThickness;

    /// <summary>
    /// The one outline thickness a cat (Kuro or Shiro) uses on his body, both ears and the tail, so he reads as one silhouette and not a ring
    /// with thin ears: <see cref="CatNormalThickness"/> on the dark grounds, and <see cref="CatLightGroundThickness"/> for Shiro on the light
    /// Matcha ground. Kuro keeps <see cref="CatNormalThickness"/> there: his dark body is the contrast on a light ground, so a thick rim would only
    /// make a heavy ring.
    /// </summary>
    public static double CatThickness(MascotKind kind, string? theme) =>
        kind is MascotKind.Shiro && IsLightGround(theme) ? CatLightGroundThickness : CatNormalThickness;

    /// <summary>The rim colour for the light ground: a neutral grey becomes <see cref="DarkNeutral"/>, a coloured edge is darkened until it is dark enough.</summary>
    public static string OnLightGround(string edge)
    {
        if (!TryParse(edge, out int r, out int g, out int b)) return DarkNeutral;
        if (MascotTint.Luminance(r, g, b) <= MaxLuminance) return Hex(r, g, b);
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (max == 0 || (max - min) / (double)max < 0.12) return DarkNeutral;
        for (int i = 0; i < 40 && MascotTint.Luminance(r, g, b) > MaxLuminance; i++)
        {
            r = r * 9 / 10; g = g * 9 / 10; b = b * 9 / 10;
        }
        return Hex(r, g, b);
    }

    private static bool TryParse(string? hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (hex == null) return false;
        string h = hex.Trim().TrimStart('#');
        if (h.Length == 8) h = h.Substring(2);
        if (h.Length != 6) return false;
        var style = System.Globalization.NumberStyles.HexNumber;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return int.TryParse(h.Substring(0, 2), style, culture, out r) && int.TryParse(h.Substring(2, 2), style, culture, out g)
            && int.TryParse(h.Substring(4, 2), style, culture, out b);
    }

    private static string Hex(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";
}
