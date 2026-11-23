using MochiDock.Core;

public class MascotHeadsetTests
{
    private static readonly string[] Themes = { "Default", "Hojicha", "Matcha", "KuroNight" };

    [Theory]
    [InlineData("Default")]
    [InlineData("Hojicha")]
    [InlineData("Matcha")]
    [InlineData("unknown")]
    public void GreyMochiIsGraphiteAndSilverOnEveryThemeButKuroNight(string theme)
    {
        var h = MascotHeadset.For(MascotKind.Mochi, theme);
        Assert.Equal(new HeadsetLook("#3A3D4A", "#EBEBF0", "#3A3D4A", "#EBEBF0"), h);
    }

    [Fact]
    public void GreyMochiOnKuroNightKeepsHisStormAndYellow() =>
        Assert.Equal(new HeadsetLook("#2A2C3D", "#FFD43B", "#2A2C3D", "#FFD43B"), MascotHeadset.For(MascotKind.Mochi, "KuroNight"));

    [Fact]
    public void MatchaMochiIsDeepMatchaAndFoamOnEveryTheme()
    {
        foreach (var theme in Themes)
            Assert.Equal(new HeadsetLook("#365D2A", "#F5F0DF", "#365D2A", "#F5F0DF"), MascotHeadset.For(MascotKind.MatchaMochi, theme));
    }

    [Fact]
    public void HojichaMochiIsRoastAndCaramelOnEveryTheme()
    {
        foreach (var theme in Themes)
            Assert.Equal(new HeadsetLook("#5A4030", "#E0A96D", "#5A4030", "#E0A96D"), MascotHeadset.For(MascotKind.HojichaMochi, theme));
    }

    [Fact]
    public void KuroIsTheSameOnEveryTheme()
    {
        foreach (var theme in Themes)
            Assert.Equal(new HeadsetLook("#2A2C3D", "#FFD43B", "#2A2C3D", "#FFD43B"), MascotHeadset.For(MascotKind.Kuro, theme));
    }

    [Theory]
    [InlineData("Default", "#B8B9C0")]
    [InlineData("Hojicha", "#B8B9C0")]
    [InlineData("KuroNight", "#B8B9C0")]
    [InlineData("Matcha", "#70727E")]
    public void ShiroIsWhiteWithAnIceBlueRing(string theme, string bandRim)
    {
        var h = MascotHeadset.For(MascotKind.Shiro, theme);
        Assert.Equal("#F7F7FA", h.Band);
        Assert.Equal("#F7F7FA", h.Cup);
        Assert.Equal(bandRim, h.BandRim);
        Assert.Equal("#5AA9E6", h.CupRing);
    }

    [Fact]
    public void NoHeadsetIsBrightBlueOrRedAnymore()
    {
        foreach (var kind in Enum.GetValues<MascotKind>())
            foreach (var theme in Themes)
            {
                var h = MascotHeadset.For(kind, theme);
                foreach (var hex in new[] { h.Band, h.Cup })
                {
                    Assert.NotEqual("#0A84FF", hex);
                    Assert.NotEqual("#E5484D", hex);
                }
            }
    }
}
