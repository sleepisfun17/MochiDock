namespace MochiDock.Core;

/// <summary>One resolution a display offers, described by size name (FHD, QHD, 4K ...) and screen shape (16:9, 16:10 ...).</summary>
public sealed record GamingResolutionOption(string SizeName, int TierHeight, string Aspect, int Width, int Height)
{
    /// <summary>Text stored in settings and profiles, e.g. <c>1728x1080</c>.</summary>
    public string Value => $"{Width}x{Height}";
    public string Label => $"{SizeName} · {Aspect} · {Width}×{Height}";
}

/// <summary>
/// Builds the gaming-resolution choices from the modes a display actually offers, so the user picks a size (FHD, QHD, 4K) and a
/// shape (16:9, 16:10 ...) instead of typing numbers. The size is the picture height, so FHD stays 1080 rows tall whatever the
/// shape: FHD 16:10 is 1728x1080. Only exact modes the display reports are listed (a combination it lacks simply does not appear).
/// </summary>
public static class GamingResolutionOptions
{
    static readonly (int Height, string Name)[] Tiers =
    {
        (720, "HD"), (768, "WXGA"), (900, "HD+"), (1050, "WSXGA+"), (1080, "FHD"), (1200, "WUXGA"), (1440, "QHD"), (1600, "WQXGA"), (2160, "4K"), (4320, "8K"),
    };

    static readonly string[] AspectOrder = { "16:9", "16:10", "21:9", "32:9", "4:3", "5:4" };

    const int MinimumHeight = 720;

    public static string SizeName(int height) => Tiers.FirstOrDefault(t => t.Height == height).Name ?? $"{height}p";

    public static IReadOnlyList<GamingResolutionOption> Build(IEnumerable<(int Width, int Height)> supported) =>
        supported
            .Where(m => m.Width > 0 && m.Height >= MinimumHeight && m.Width >= m.Height)
            .Distinct()
            .Select(m => new GamingResolutionOption(SizeName(m.Height), m.Height, DisplayResolutionPolicy.GetAspectRatio(m.Width, m.Height), m.Width, m.Height))
            .OrderByDescending(o => o.TierHeight)
            .ThenBy(o => AspectRank(o.Aspect))
            .ThenByDescending(o => o.Width)
            .ToList();

    /// <summary>The size choices, tallest first (one per picture height).</summary>
    public static IReadOnlyList<(int TierHeight, string SizeName)> Sizes(IReadOnlyList<GamingResolutionOption> options) =>
        options.Select(o => (o.TierHeight, o.SizeName)).Distinct().OrderByDescending(s => s.TierHeight).ToList();

    /// <summary>The shapes this display offers at the given size.</summary>
    public static IReadOnlyList<GamingResolutionOption> AspectsFor(IReadOnlyList<GamingResolutionOption> options, int tierHeight) =>
        options.Where(o => o.TierHeight == tierHeight).OrderBy(o => AspectRank(o.Aspect)).ThenByDescending(o => o.Width).ToList();

    public static GamingResolutionOption? Find(IReadOnlyList<GamingResolutionOption> options, string? value)
    {
        if (!DisplayResolutionPolicy.TryParse(value, out var width, out var height)) return null;
        return options.FirstOrDefault(o => o.Width == width && o.Height == height);
    }

    /// <summary>
    /// A sensible gaming resolution for a new computer: the native height in 16:10 (a taller, narrower picture some players prefer)
    /// when the display offers it, otherwise null (the caller keeps native). Never invents a mode the display does not report.
    /// </summary>
    public static GamingResolutionOption? SuggestStretched(IReadOnlyList<GamingResolutionOption> options, (int Width, int Height) native) =>
        options.FirstOrDefault(o => o.Height == native.Height && o.Aspect == "16:10" && o.Width != native.Width);

    /// <summary>
    /// The gaming resolution the hotkey, tray and settings use on this display. A stored value is used only when the display offers
    /// it (null otherwise, so nothing switches to a mode the display lacks). With nothing stored, as on a new install, the
    /// stretched 16:10 suggestion is used when the display offers one.
    /// </summary>
    public static GamingResolutionOption? Resolve(IReadOnlyList<GamingResolutionOption> options, (int Width, int Height) native, string? stored) =>
        DisplayResolutionPolicy.TryParse(stored, out _, out _) ? Find(options, stored) : SuggestStretched(options, native);

    static int AspectRank(string aspect)
    {
        var index = Array.IndexOf(AspectOrder, aspect);
        return index < 0 ? AspectOrder.Length : index;
    }
}
