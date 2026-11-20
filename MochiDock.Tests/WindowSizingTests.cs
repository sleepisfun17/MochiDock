using MochiDock.Core;

public class WindowSizingTests
{
    [Fact]
    public void DefaultsMatchTheEarlierScript()
    {
        var d = WindowSizing.Defaults();
        Assert.Equal(new[] { "1920x1080", "2560x1440", "1080x1920", "720x1280" }, d.Select(p => $"{p.Width}x{p.Height}"));
        Assert.Equal(new[] { "Ctrl+Alt+1", "Ctrl+Alt+2", "Ctrl+Alt+3", "Ctrl+Alt+4" }, d.Select(p => p.Hotkey));
        Assert.All(d, p => Assert.True(p.Enabled));
        Assert.Equal("Ctrl+Alt+F", WindowSizing.DefaultFlipHotkey);
    }

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData(" 1920 × 1080 ", 1920, 1080)]
    [InlineData("720*1280", 720, 1280)]
    [InlineData("2560X1440", 2560, 1440)]
    public void ReadsSizes(string text, int w, int h)
    {
        Assert.True(WindowSizing.TryParseSize(text, out int width, out int height, out _));
        Assert.Equal((w, h), (width, height));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1920")]
    [InlineData("1920x")]
    [InlineData("100x100")]
    [InlineData("20000x1080")]
    [InlineData("big")]
    public void RejectsBadSizes(string text) => Assert.False(WindowSizing.TryParseSize(text, out _, out _, out var error) || error == null);

    [Fact]
    public void CentresInTheWorkAreaLikeTheScript()
    {
        // 2560 x 1440 monitor with a 48-pixel taskbar at the bottom.
        Assert.Equal((320, 156), WindowSizing.Place(0, 0, 2560, 1392, 1920, 1080));
        // A second monitor to the right.
        Assert.Equal((2560 + 600, 56), WindowSizing.Place(2560, 0, 4480, 1032, 720, 920));
    }

    [Fact]
    public void ALargerWindowKeepsItsTitleBarOnScreen()
    {
        // 2560 x 1440 window on a 1392-pixel-high work area: it fits across but not down, so it sits at the top.
        Assert.Equal((0, 0), WindowSizing.Place(0, 0, 2560, 1392, 2560, 1440));
        // A portrait 1080 x 1920 window on a 1080p work area.
        Assert.Equal((420, 0), WindowSizing.Place(0, 0, 1920, 1032, 1080, 1920));
    }

    [Fact]
    public void FindsDuplicateAndReservedShortcuts()
    {
        var presets = new List<WindowSizePreset>
        {
            new(1920, 1080, "Ctrl+Alt+1"),
            new(2560, 1440, "Ctrl+Alt+1"),
            new(1080, 1920, "Ctrl+Alt+R"),
            new(720, 1280, "Ctrl+Alt+4", enabled: false),
            new(800, 600, "Ctrl+Alt+4"),
        };
        var reserved = new Dictionary<string, string> { ["Ctrl+Alt+R"] = "screen resolution" };
        var problems = WindowSizing.FindShortcutProblems(presets, "Ctrl+Alt+F", true, reserved);
        Assert.Contains("used twice", problems[0]);
        Assert.Contains("used twice", problems[1]);
        Assert.Contains("screen resolution", problems[2]);
        Assert.False(problems.ContainsKey(3)); // disabled presets don't claim their shortcut
        Assert.False(problems.ContainsKey(4));
        Assert.False(problems.ContainsKey(-1));
    }

    [Fact]
    public void FlipCannotReuseAPresetShortcut()
    {
        var problems = WindowSizing.FindShortcutProblems(WindowSizing.Defaults(), "Ctrl+Alt+2", true, new Dictionary<string, string>());
        Assert.Contains("used twice", problems[-1]);
        Assert.Empty(WindowSizing.FindShortcutProblems(WindowSizing.Defaults(), "Ctrl+Alt+2", false, new Dictionary<string, string>()));
    }
}

public class WindowSizeSettingsTests
{
    [Fact]
    public void NewInstallsStartWithTheShortcutsOffButKeepTheScriptSizes()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<MochiDock.Models.AppSettings>("""{"ResolutionHotkey":"Ctrl+Alt+R"}""")!;
        Assert.False(settings.EnableWindowSizeShortcuts); // nobody gets Ctrl+Alt+1-4 taken system-wide without asking for it
        Assert.Equal(4, settings.WindowSizePresets.Count);
        Assert.Equal("Ctrl+Alt+F", settings.WindowFlipHotkey);
    }

    [Fact]
    public void EditedPresetsSurviveASaveAndLoad()
    {
        var settings = new MochiDock.Models.AppSettings { WindowSizePresets = new() { new(1280, 720, "Ctrl+Alt+7", enabled: false) }, EnableWindowFlip = false };
        var loaded = System.Text.Json.JsonSerializer.Deserialize<MochiDock.Models.AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        var preset = Assert.Single(loaded.WindowSizePresets);
        Assert.Equal((1280, 720, "Ctrl+Alt+7", false), (preset.Width, preset.Height, preset.Hotkey, preset.Enabled));
        Assert.False(loaded.EnableWindowFlip);
    }
}

public class PublicDefaultsTests
{
    [Fact]
    public void NewInstallsStartWithOptInFeaturesOff()
    {
        var s = new MochiDock.Models.AppSettings();
        Assert.False(s.EnableWindowSizeShortcuts);
        Assert.False(s.EnablePrayerTimes);
        Assert.False(s.ShowJapanHolidays);
        Assert.False(s.ShowIndonesiaHolidays);
        Assert.Equal("", s.RemoteGowaUrl);
    }

    [Fact]
    public void ASavedSettingsFileKeepsItsChoices()
    {
        const string json = """{"EnableWindowSizeShortcuts":true,"EnablePrayerTimes":true,"ShowJapanHolidays":true,"ShowIndonesiaHolidays":true,"RemoteGowaUrl":"http://192.168.1.5:3099"}""";
        var s = System.Text.Json.JsonSerializer.Deserialize<MochiDock.Models.AppSettings>(json)!;
        Assert.True(s.EnableWindowSizeShortcuts && s.EnablePrayerTimes && s.ShowJapanHolidays && s.ShowIndonesiaHolidays);
        Assert.Equal("http://192.168.1.5:3099", s.RemoteGowaUrl);
    }
}
