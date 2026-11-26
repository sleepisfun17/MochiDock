using System.Text.Json;
using MochiDock.Core;
using MochiDock.Models;

public class MascotChoiceTests
{
    [Theory]
    [InlineData("Auto", "Auto")]
    [InlineData("mochi", "Mochi")]
    [InlineData("MatchaMochi", "MatchaMochi")]
    [InlineData("matcha mochi", "MatchaMochi")]
    [InlineData(" HojichaMochi ", "HojichaMochi")]
    [InlineData("Hojicha Mochi", "HojichaMochi")]
    [InlineData(" KURO ", "Kuro")]
    [InlineData("shiro", "Shiro")]
    [InlineData("", "Auto")]
    [InlineData(null, "Auto")]
    [InlineData("Pikachu", "Auto")]
    [InlineData("Matcha", "Auto")]
    public void Normalizes(string? input, string expected) => Assert.Equal(expected, MascotChoice.Normalize(input));

    // Auto is the theme's own mascot; existing users on Auto see exactly what they saw before.
    [Theory]
    [InlineData("Default", MascotKind.Mochi)]
    [InlineData("Hojicha", MascotKind.HojichaMochi)]
    [InlineData("Matcha", MascotKind.MatchaMochi)]
    [InlineData("KuroNight", MascotKind.Kuro)]
    [InlineData("Kuromame", MascotKind.Kuro)]
    [InlineData("unknown", MascotKind.Mochi)]
    [InlineData(null, MascotKind.Mochi)]
    public void AutoIsTheThemesOwnMascot(string? theme, MascotKind expected) => Assert.Equal(expected, MascotChoice.Resolve("Auto", theme));

    [Theory]
    [InlineData("Default")]
    [InlineData("Hojicha")]
    [InlineData("Matcha")]
    [InlineData("KuroNight")]
    public void AChosenMascotIsTheSameOnEveryTheme(string theme)
    {
        Assert.Equal(MascotKind.Mochi, MascotChoice.Resolve("Mochi", theme));
        Assert.Equal(MascotKind.MatchaMochi, MascotChoice.Resolve("MatchaMochi", theme));
        Assert.Equal(MascotKind.HojichaMochi, MascotChoice.Resolve("HojichaMochi", theme));
        Assert.Equal(MascotKind.Kuro, MascotChoice.Resolve("Kuro", theme));
        Assert.Equal(MascotKind.Shiro, MascotChoice.Resolve("Shiro", theme));
        Assert.Equal(MascotChoice.Resolve("Auto", theme), MascotChoice.Resolve("nonsense", theme));
    }

    [Fact]
    public void OnlyTheThreeMochisAreMochi()
    {
        Assert.True(MascotChoice.IsMochi(MascotKind.Mochi));
        Assert.True(MascotChoice.IsMochi(MascotKind.MatchaMochi));
        Assert.True(MascotChoice.IsMochi(MascotKind.HojichaMochi));
        Assert.False(MascotChoice.IsMochi(MascotKind.Kuro));
        Assert.False(MascotChoice.IsMochi(MascotKind.Shiro));
    }

    [Fact]
    public void OldSettingsWithoutTheKeyLoadAsAuto()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"CapsuleTheme\":\"Matcha\"}")!;
        Assert.Equal("Auto", settings.Mascot);
        Assert.Equal("Auto", new AppSettings().Mascot);
    }

    [Fact]
    public void SettingsFromTheLastBuildStillLoad()
    {
        // The previous build stored the theme as "Kuromame" and the mascot as Auto, Mochi, Kuro or Shiro.
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"CapsuleTheme\":\"Kuromame\",\"Mascot\":\"Shiro\"}")!;
        Assert.Equal("KuroNight", settings.CapsuleTheme);
        Assert.Equal("Shiro", settings.Mascot);
    }

    [Theory]
    [InlineData("Shiro", "Shiro")]
    [InlineData("kuro", "Kuro")]
    [InlineData("matcha mochi", "MatchaMochi")]
    [InlineData("HojichaMochi", "HojichaMochi")]
    [InlineData("whatever", "Auto")]
    public void SettingRoundTrips(string input, string expected)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new { Mascot = input }))!;
        Assert.Equal(expected, settings.Mascot);
        Assert.Equal(expected, JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.Mascot);
    }

    [Fact]
    public void CatMouthDefaultsToOnRoundTripsAndMatchesMascotSearch()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"CapsuleTheme\":\"Default\"}")!;
        Assert.True(old.ShowCatMouth);

        var off = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { ShowCatMouth = false }))!;
        Assert.False(off.ShowCatMouth);

        Assert.True(SettingsSections.Matches(SettingsSections.Find(SettingsSections.Mascot)!, "mouth"));
    }
}
