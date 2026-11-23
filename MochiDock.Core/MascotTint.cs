namespace MochiDock.Core;

/// <summary>The colours Kuro (Mochi in the KuroNight theme) wears for one face: hex strings, "#RRGGBB".</summary>
public readonly record struct KuroTint(string BodyTop, string BodyBottom, string Rim, string Glow);

/// <summary>
/// Kuro is a black cat, so his body is always charcoal. A face's own colour (Angry red, Hot orange, Freezing blue...) goes into the thin
/// rim and the soft glow instead, and the neutral faces glow electric yellow.
/// </summary>
public static class MascotTint
{
    public const string CharcoalTop = "#3B3D52";
    public const string CharcoalBottom = "#23253A";
    public const string ElectricYellow = "#FFD43B";

    // An edge colour with less saturation than this belongs to a neutral (grey-white) face.
    private const double NeutralSaturation = 0.12;
    // The rim has to stand out against the storm-night ground, so a dark face colour is lifted to at least this relative luminance.
    private const double MinRimLuminance = 0.3;

    /// <summary>The KuroNight look for a face whose normal (Default theme) tint is the top, bottom, edge and cheek colours.</summary>
    public static KuroTint KuroNight(IReadOnlyList<string> normalTint)
    {
        if (normalTint == null || normalTint.Count < 3) return Neutral();
        if (!TryParse(normalTint[2], out int r, out int g, out int b)) return Neutral();
        if (Saturation(r, g, b) < NeutralSaturation) return Neutral();
        var (lr, lg, lb) = Lift(r, g, b);
        return new KuroTint(CharcoalTop, CharcoalBottom, Hex(lr, lg, lb), Hex(r, g, b));
    }

    private static KuroTint Neutral() => new(CharcoalTop, CharcoalBottom, ElectricYellow, ElectricYellow);

    private static bool TryParse(string? hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (hex == null) return false;
        string h = hex.Trim().TrimStart('#');
        if (h.Length == 8) h = h.Substring(2);   // #AARRGGBB: the colour part
        if (h.Length != 6) return false;
        var hexStyle = System.Globalization.NumberStyles.HexNumber;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return int.TryParse(h.Substring(0, 2), hexStyle, culture, out r)
            && int.TryParse(h.Substring(2, 2), hexStyle, culture, out g)
            && int.TryParse(h.Substring(4, 2), hexStyle, culture, out b);
    }

    private static string Hex(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";

    private static double Saturation(int r, int g, int b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return max == 0 ? 0 : (max - min) / (double)max;
    }

    private static double Channel(int v)
    {
        double c = v / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>WCAG relative luminance of an RGB colour.</summary>
    public static double Luminance(int r, int g, int b) => 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);

    // Mixes the colour toward white in small steps until it is bright enough for a rim.
    private static (int r, int g, int b) Lift(int r, int g, int b)
    {
        for (int step = 0; step < 24 && Luminance(r, g, b) < MinRimLuminance; step++)
        {
            r = Math.Min(255, r + (255 - r) / 8 + 1);
            g = Math.Min(255, g + (255 - g) / 8 + 1);
            b = Math.Min(255, b + (255 - b) / 8 + 1);
        }
        return (r, g, b);
    }
}
