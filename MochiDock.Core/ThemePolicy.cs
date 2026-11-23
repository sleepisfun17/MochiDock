namespace MochiDock.Core;

/// <summary>The capsule themes: Default (dark), Hojicha (warm dark), KuroNight (storm-night dark) and Matcha (light). Unknown names fall back to Default.</summary>
public static class ThemePolicy
{
    public const string Default = "Default";
    public const string Matcha = "Matcha";
    public const string Hojicha = "Hojicha";
    public const string KuroNight = "KuroNight";

    public static string Normalize(string? theme)
    {
        string t = theme?.Trim() ?? "";
        if (string.Equals(t, Matcha, StringComparison.OrdinalIgnoreCase)) return Matcha;
        if (string.Equals(t, Hojicha, StringComparison.OrdinalIgnoreCase)) return Hojicha;
        // "Kuro Night" is the display name; "Kuromame" is what earlier builds stored, so old settings files keep their theme.
        string compact = t.Replace(" ", "");
        if (string.Equals(compact, KuroNight, StringComparison.OrdinalIgnoreCase) || string.Equals(compact, "Kuromame", StringComparison.OrdinalIgnoreCase)) return KuroNight;
        return Default;
    }

    /// <summary>True for the dark themes (Default, Hojicha and KuroNight): dark window captions and the XAML's own dark colours.</summary>
    public static bool IsDark(string? theme) => Normalize(theme) != Matcha;
}
