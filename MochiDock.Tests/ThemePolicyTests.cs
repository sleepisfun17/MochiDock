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
    public void SettingsNormalizeAndRoundTrip(string? input, string expected)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new { CapsuleTheme = input }))!;
        Assert.Equal(expected,settings.CapsuleTheme);
        Assert.Equal(expected,JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.CapsuleTheme);
    }
}
