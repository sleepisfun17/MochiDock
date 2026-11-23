namespace MochiDock.Core;

/// <summary>
/// The music note that floats up beside a cat (Kuro or Shiro) while music plays. It drifts on the tail side, outside the body, so its colour
/// is read against the theme's capsule ground: a soft violet on the dark grounds, a deep violet on the light Matcha ground.
/// </summary>
public static class MascotNote
{
    public const string OnDark = "#B58CFF";
    public const string OnLight = "#6E4BB8";

    /// <summary>The capsule ground of each theme (IslandGroundBrush).</summary>
    public static string GroundFor(string? theme) => ThemePolicy.Normalize(theme) switch
    {
        ThemePolicy.Matcha => "#EFE6CF",
        ThemePolicy.Hojicha => "#14100D",
        ThemePolicy.KuroNight => "#0C0D18",
        _ => "#000000",
    };

    public static string InkFor(string? theme) => MascotRim.IsLightGround(theme) ? OnLight : OnDark;
}
