namespace MochiDock.Core;

/// <summary>The body colours Shiro (the white cat) wears for one face: top, bottom, edge and cheek, hex strings.</summary>
public readonly record struct ShiroLook(string Top, string Bottom, string Edge, string Cheek);

/// <summary>
/// Shiro wears Mochi's normal (Default theme) face tints on every theme and never turns Matcha green. The neutral faces get a white body;
/// faces with a colour of their own keep it, except yellow ones. No cat ever turns yellow: the Electric face is treated as neutral, and a
/// yellow face (Victory's gold, the cream of Surprised and Sunny) keeps the white body with its colour moved into the rim, as Kuro does.
/// </summary>
public static class ShiroTint
{
    public const string WhiteTop = "#F4F4F8";
    public const string WhiteBottom = "#DCDDE3";
    public const string Rim = "#B8B9C0";
    public const string RimOnMatcha = "#70727E";   // darker (and drawn thicker), so the white cat stays visible on Matcha's light ground
    public const string GoldRimOnMatcha = "#8F6B0E";   // a yellow face's gold rim on Matcha: about 3.9:1 on its light ground

    private const double NeutralSaturation = 0.12;
    // Edge hues from orange-yellow to yellow-green (degrees) with real colour in them count as yellow faces.
    private const double YellowHueFrom = 35, YellowHueTo = 70, YellowSaturation = 0.3;

    public static ShiroLook For(IReadOnlyList<string> normalTint, string? theme, bool forceNeutral = false)
    {
        bool matcha = ThemePolicy.Normalize(theme) == ThemePolicy.Matcha;
        string rim = matcha ? RimOnMatcha : Rim;
        string cheek = normalTint is { Count: > 3 } ? normalTint[3] : "#73FF7896";
        if (normalTint == null || normalTint.Count < 3) return new(WhiteTop, WhiteBottom, rim, cheek);
        if (forceNeutral || !TryHueSaturation(normalTint[2], out double hue, out double saturation) || saturation < NeutralSaturation)
            return new(WhiteTop, WhiteBottom, rim, cheek);
        if (hue >= YellowHueFrom && hue <= YellowHueTo && saturation >= YellowSaturation)
            return new(WhiteTop, WhiteBottom, matcha ? GoldRimOnMatcha : normalTint[2], cheek);
        return new(normalTint[0], normalTint[1], normalTint[2], cheek);
    }

    private static bool TryHueSaturation(string edge, out double hue, out double saturation)
    {
        hue = saturation = 0;
        string h = edge.Trim().TrimStart('#');
        if (h.Length == 8) h = h.Substring(2);
        if (h.Length != 6) return false;
        var style = System.Globalization.NumberStyles.HexNumber;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (!int.TryParse(h.Substring(0, 2), style, culture, out int r) || !int.TryParse(h.Substring(2, 2), style, culture, out int g)
            || !int.TryParse(h.Substring(4, 2), style, culture, out int b)) return false;
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (max == 0) return true;
        double delta = max - min;
        saturation = delta / max;
        if (delta == 0) return true;
        hue = max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        return true;
    }
}
