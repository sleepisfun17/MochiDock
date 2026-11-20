using MochiDock.Core;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+R", 3u, 0x52u, "Ctrl+Alt+R")]
    [InlineData("alt+ctrl+r", 3u, 0x52u, "Ctrl+Alt+R")]
    [InlineData(" Control + Shift + F9 ", 6u, 0x78u, "Ctrl+Shift+F9")]
    [InlineData("Win+5", 8u, 0x35u, "Win+5")]
    [InlineData("Ctrl+Alt+Win+Shift+f24", 15u, 0x87u, "Ctrl+Alt+Shift+Win+F24")]
    public void ParsesAndCanonicalises(string text, uint modifiers, uint vk, string canonical)
    {
        Assert.True(HotkeyParser.TryParse(text, out var hotkey, out var error));
        Assert.Null(error);
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(vk, hotkey.VirtualKey);
        Assert.Equal(canonical, hotkey.Text);
    }

    [Theory]
    [InlineData("Ctrl+Alt+`", 3u, 0xC0u)]
    [InlineData("Win+-", 8u, 0xBDu)]
    [InlineData("Ctrl+Shift+/", 6u, 0xBFu)]
    [InlineData("Alt+\\", 1u, 0xDCu)]
    [InlineData("Ctrl+;", 2u, 0xBAu)]
    [InlineData("Ctrl+'", 2u, 0xDEu)]
    public void ParsesPunctuationKeys(string text, uint modifiers, uint vk)
    {
        Assert.True(HotkeyParser.TryParse(text, out var hotkey, out _));
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(vk, hotkey.VirtualKey);
        Assert.Equal(text, hotkey.Text);
        Assert.Equal(text[(text.LastIndexOf('+') + 1)..] is { Length: 1 } name ? name : null, HotkeyParser.PunctuationName(vk));
    }

    [Theory]
    [InlineData("`")]          // punctuation still needs a modifier
    [InlineData("Shift+`")]
    [InlineData("Ctrl+Alt+++")] // "+" cannot be the key
    public void PunctuationStillNeedsAModifierAndNoPlus(string text) =>
        Assert.False(HotkeyParser.TryParse(text, out _, out _));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("R")]                // no modifier
    [InlineData("Shift+R")]          // Shift alone would interfere with typing
    [InlineData("Ctrl+Alt")]         // no key
    [InlineData("Ctrl+R+T")]         // two keys
    [InlineData("Ctrl+Alt+Delete")]  // unsupported key
    [InlineData("Ctrl+F25")]
    [InlineData("Alt+Shift+S")]      // reserved for the Script reader
    [InlineData("shift+alt+s")]
    public void RejectsInvalidOrReserved(string? text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void DefaultIsValid() => Assert.True(HotkeyParser.TryParse(HotkeyParser.Default, out var h, out _) && h.Text == HotkeyParser.Default);

    [Fact]
    public void FormatBuildsCanonicalTextOrNull()
    {
        Assert.Equal("Ctrl+Shift+F9", HotkeyParser.Format(true, false, true, false, "F9"));
        Assert.Null(HotkeyParser.Format(false, false, false, false, "R"));
    }
}
