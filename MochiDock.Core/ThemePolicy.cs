namespace MochiDock.Core;

public static class ThemePolicy
{
    public static string Normalize(string? theme) =>
        string.Equals(theme?.Trim(), "Matcha", StringComparison.OrdinalIgnoreCase) ? "Matcha" : "Default";
}
