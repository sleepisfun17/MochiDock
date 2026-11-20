using System.Globalization;
using System.Text.RegularExpressions;

namespace MochiDock.Core;

/// <summary>A window size the active window can be set to with a global shortcut (Settings > Gaming &amp; Display > Window sizes).</summary>
public sealed class WindowSizePreset
{
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Global shortcut text as <see cref="HotkeyParser"/> writes it, for example "Ctrl+Alt+1"; empty for none.</summary>
    public string Hotkey { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public WindowSizePreset() { }
    public WindowSizePreset(int width, int height, string hotkey, bool enabled = true)
    {
        Width = width;
        Height = height;
        Hotkey = hotkey;
        Enabled = enabled;
    }

    public WindowSizePreset Clone() => new(Width, Height, Hotkey, Enabled);
}

/// <summary>Pure rules for the window-size shortcuts: defaults, size parsing, placement on a monitor and shortcut conflicts.</summary>
public static class WindowSizing
{
    public const string DefaultFlipHotkey = "Ctrl+Alt+F";
    public const int MinSide = 200;
    public const int MaxSide = 16384;
    /// <summary>One global hotkey id is reserved per preset; more presets than this are kept but get no shortcut.</summary>
    public const int MaxPresets = 9;

    /// <summary>The sizes the earlier AutoHotkey script used: two landscape and two portrait (vertical video) sizes.</summary>
    public static List<WindowSizePreset> Defaults() => new()
    {
        new(1920, 1080, "Ctrl+Alt+1"),
        new(2560, 1440, "Ctrl+Alt+2"),
        new(1080, 1920, "Ctrl+Alt+3"),
        new(720, 1280, "Ctrl+Alt+4"),
    };

    static readonly Regex SizePattern = new(@"^\s*(\d{2,5})\s*[x×X*,]\s*(\d{2,5})\s*$", RegexOptions.CultureInvariant);

    /// <summary>Reads "1920x1080", "1920 × 1080" or "1920*1080"; both sides must be between <see cref="MinSide"/> and <see cref="MaxSide"/>.</summary>
    public static bool TryParseSize(string? text, out int width, out int height, out string? error)
    {
        width = height = 0;
        error = null;
        var m = SizePattern.Match(text ?? "");
        if (!m.Success) { error = "Type the size as width x height, for example 1920x1080."; return false; }
        width = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        height = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        if (width < MinSide || height < MinSide || width > MaxSide || height > MaxSide)
        {
            error = $"Each side must be between {MinSide} and {MaxSide} pixels.";
            return false;
        }
        return true;
    }

    public static string FormatSize(int width, int height) => $"{width} × {height}";

    /// <summary>
    /// Where a window of the given size goes on a monitor's work area (the screen minus the taskbar): centred, like the script did.
    /// A window larger than the work area is aligned to the work area's top or left edge instead, so its title bar stays reachable.
    /// </summary>
    public static (int X, int Y) Place(int workLeft, int workTop, int workRight, int workBottom, int width, int height)
    {
        int workWidth = workRight - workLeft, workHeight = workBottom - workTop;
        int x = width <= workWidth ? workLeft + (workWidth - width) / 2 : workLeft;
        int y = height <= workHeight ? workTop + (workHeight - height) / 2 : workTop;
        return (x, y);
    }

    /// <summary>
    /// Problems with the shortcuts of the enabled presets and the flip: unreadable text, a shortcut used twice, or one already taken by
    /// another MochiDock shortcut (<paramref name="reserved"/>, for example the resolution toggle). Keys are preset indexes, or -1 for the flip.
    /// </summary>
    public static Dictionary<int, string> FindShortcutProblems(IReadOnlyList<WindowSizePreset> presets, string? flipHotkey, bool flipEnabled,
        IReadOnlyDictionary<string, string> reserved)
    {
        var problems = new Dictionary<int, string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        void Check(int index, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!HotkeyParser.TryParse(text, out var hotkey, out var error)) { problems[index] = error ?? "Unreadable shortcut."; return; }
            if (reserved.TryGetValue(hotkey.Text, out var owner)) { problems[index] = $"{hotkey.Text} is already the {owner} shortcut."; return; }
            if (seen.TryGetValue(hotkey.Text, out int first)) { problems[index] = $"{hotkey.Text} is used twice."; problems.TryAdd(first, $"{hotkey.Text} is used twice."); return; }
            seen[hotkey.Text] = index;
        }

        for (int i = 0; i < presets.Count; i++)
            if (presets[i].Enabled) Check(i, presets[i].Hotkey);
        if (flipEnabled) Check(-1, flipHotkey);
        return problems;
    }
}
