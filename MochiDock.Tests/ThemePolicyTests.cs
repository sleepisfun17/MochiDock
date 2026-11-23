using System.Text.Json;
using MochiDock.Models;

public class ThemePolicyTests
{
    [Theory]
    [InlineData("Default", "Default")]
    [InlineData("Ocean", "Default")]
    [InlineData("Rose", "Default")]
    [InlineData("", "Default")]
    [InlineData(null, "Default")]
    [InlineData("unknown", "Default")]
    [InlineData("matcha", "Matcha")]
    [InlineData(" hojicha ", "Hojicha")]
    [InlineData("HOJICHA", "Hojicha")]
    [InlineData(" kuromame ", "KuroNight")]
    [InlineData("Kuro Night", "KuroNight")]
    [InlineData(" kuro night ", "KuroNight")]
    [InlineData("KURONIGHT", "KuroNight")]
    [InlineData("KuroNight", "KuroNight")]
    [InlineData("KUROMAME", "KuroNight")]
    public void SettingsNormalizeAndRoundTrip(string? input, string expected)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new { CapsuleTheme = input }))!;
        Assert.Equal(expected,settings.CapsuleTheme);
        Assert.Equal(expected,JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.CapsuleTheme);
    }

    [Theory]
    [InlineData("Default", true)]
    [InlineData("Hojicha", true)]
    [InlineData("KuroNight", true)]
    [InlineData("Matcha", false)]
    [InlineData("whatever", true)]
    public void OnlyMatchaIsLight(string theme, bool dark) => Assert.Equal(dark, MochiDock.Core.ThemePolicy.IsDark(theme));
}
