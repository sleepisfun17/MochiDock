using MochiDock.Core;

public class MascotTintTests
{
    // The normal (Default theme) tints of a neutral face and of coloured ones: Angry, Hot, Freezing, Electric, Sunny, Sleeping.
    public static IEnumerable<object[]> Faces() => new[]
    {
        new[] { "#EDEDEF", "#C4C5CA", "#B8B9C0", "#73FF7896" },
        new[] { "#F8C9C2", "#EFA79C", "#C0392B", "#99E24B4A" },
        new[] { "#F9D2B2", "#F2B089", "#D9622B", "#99E24B4A" },
        new[] { "#8FB0F2", "#6E94E6", "#3F62C8", "#80FF7896" },
        new[] { "#FFF4C2", "#F5D96B", "#C9A227", "#66FFB347" },
        new[] { "#FFF6E0", "#F5E2B8", "#D9B35C", "#80FF7896" },
        new[] { "#E4E4E8", "#C9C9CF", "#A0A0A8", "#40FF7896" },
    }.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(Faces))]
    public void BodyIsAlwaysCharcoal(string[] tint)
    {
        var k = MascotTint.KuroNight(tint);
        Assert.Equal(MascotTint.CharcoalTop, k.BodyTop);
        Assert.Equal(MascotTint.CharcoalBottom, k.BodyBottom);
    }

    [Fact]
    public void CharcoalIsDarkAndNeverYellow()
    {
        foreach (var hex in new[] { MascotTint.CharcoalTop, MascotTint.CharcoalBottom })
        {
            Assert.True(Lum(hex) < 0.06);
            int r = Convert.ToInt32(hex.Substring(1, 2), 16), g = Convert.ToInt32(hex.Substring(3, 2), 16), b = Convert.ToInt32(hex.Substring(5, 2), 16);
            Assert.True(b >= r && b >= g, "the body leans blue, never yellow");
        }
    }

    [Theory]
    [InlineData("#B8B9C0")]
    [InlineData("#A0A0A8")]
    public void NeutralFacesGlowElectricYellow(string edge)
    {
        var k = MascotTint.KuroNight(new[] { "#EDEDEF", "#C4C5CA", edge, "#73FF7896" });
        Assert.Equal(MascotTint.ElectricYellow, k.Rim);
        Assert.Equal(MascotTint.ElectricYellow, k.Glow);
    }

    [Fact]
    public void ColouredFacesKeepTheirColourInTheGlowNotTheBody()
    {
        var angry = MascotTint.KuroNight(new[] { "#F8C9C2", "#EFA79C", "#C0392B", "#99E24B4A" });
        Assert.Equal("#C0392B", angry.Glow);
        Assert.NotEqual(MascotTint.ElectricYellow, angry.Glow);
        Assert.Equal(MascotTint.CharcoalTop, angry.BodyTop);
    }

    [Fact]
    public void DarkFaceColoursAreLiftedForTheRim()
    {
        var freezing = MascotTint.KuroNight(new[] { "#8FB0F2", "#6E94E6", "#3F62C8", "#80FF7896" });
        Assert.Equal("#3F62C8", freezing.Glow);
        Assert.NotEqual(freezing.Glow, freezing.Rim);
        Assert.True(Lum(freezing.Rim) >= 0.3);
    }

    [Fact]
    public void BadInputFallsBackToTheNeutralLook()
    {
        Assert.Equal(MascotTint.ElectricYellow, MascotTint.KuroNight(Array.Empty<string>()).Glow);
        Assert.Equal(MascotTint.ElectricYellow, MascotTint.KuroNight(new[] { "a", "b", "zzz", "d" }).Glow);
    }

    // On the light Matcha ground (#EFE6CF, luminance about 0.79) the rim alone carries the accent, so it must reach 3:1 there.
    private static double OnOat(string hex) => (0.794 + 0.05) / (Lum(hex) + 0.05);

    [Fact]
    public void OnTheLightGroundTheNeutralRimIsDeepGold()
    {
        var idle = MascotTint.OnLightGround(MascotTint.KuroNight(new[] { "#EDEDEF", "#C4C5CA", "#B8B9C0", "#73FF7896" }));
        Assert.Equal(MascotTint.GoldOnLight, idle.Rim);
        Assert.Equal(MascotTint.CharcoalTop, idle.BodyTop);
        Assert.True(OnOat(idle.Rim) >= 3.0);
    }

    [Theory]
    [InlineData("#F8C9C2", "#EFA79C", "#C0392B")]   // Angry
    [InlineData("#8FB0F2", "#6E94E6", "#3F62C8")]   // Freezing
    [InlineData("#FBEBD3", "#F2D2B0", "#C98A5B")]   // Break reminder (peach)
    public void OnTheLightGroundAFaceColourIsDarkenedFromTheFaceItself(string top, string bottom, string edge)
    {
        var dark = MascotTint.KuroNight(new[] { top, bottom, edge, "#80FF7896" });
        var light = MascotTint.OnLightGround(dark);
        Assert.Equal(MascotTint.CharcoalTop, light.BodyTop);
        Assert.NotEqual(MascotTint.GoldOnLight, light.Rim);
        Assert.True(OnOat(light.Rim) >= 3.0, $"{light.Rim} is {OnOat(light.Rim):0.00}:1 on oat");
    }

    private static double Lum(string hex) =>
        MascotTint.Luminance(Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16));
}
