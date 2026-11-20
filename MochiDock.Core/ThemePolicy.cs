namespace MochiDock.Core;

/// <summary>The capsule themes: Default (dark), Hojicha (warm dark) and Matcha (light). Unknown names fall back to Default.</summary>
public static class ThemePolicy
{
    public const string Default = "Default";
    public const string Matcha = "Matcha";
    public const string Hojicha = "Hojicha";

    public static string Normalize(string? theme)
    {
        string t = theme?.Trim() ?? "";
        if (string.Equals(t, Matcha, StringComparison.OrdinalIgnoreCase)) return Matcha;
        if (string.Equals(t, Hojicha, StringComparison.OrdinalIgnoreCase)) return Hojicha;
        return Default;
    }

    /// <summary>True for the dark themes (Default and Hojicha): dark window captions and the XAML's own dark colours.</summary>
    public static bool IsDark(string? theme) => Normalize(theme) != Matcha;
}
