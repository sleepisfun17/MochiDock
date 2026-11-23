using MochiDock.Core;

public class MascotRimTests
{
    [Theory]
    [InlineData(MascotKind.Mochi, true)]
    [InlineData(MascotKind.HojichaMochi, true)]
    [InlineData(MascotKind.Shiro, true)]
    [InlineData(MascotKind.MatchaMochi, true)]
    [InlineData(MascotKind.Kuro, false)]
    public void EveryMascotButKuroNeedsItOnMatcha(MascotKind kind, bool needs)
    {
        Assert.Equal(needs, MascotRim.NeedsDarkRim(kind, "Matcha"));
        // the light-bodied mascots 1.5 (Shiro on body, ears and tail alike); Kuro keeps his normal 1.0 (his dark body is the contrast)
        double expected = kind is MascotKind.Kuro ? MascotRim.CatNormalThickness : 1.5;
        Assert.Equal(expected, MascotRim.Thickness(kind, "Matcha"));
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Hojicha")]
    [InlineData("KuroNight")]
    public void DarkGroundsKeepTheNormalRim(string theme)
    {
        foreach (var kind in Enum.GetValues<MascotKind>())
        {
            Assert.False(MascotRim.NeedsDarkRim(kind, theme));
            Assert.Equal(kind is MascotKind.Kuro or MascotKind.Shiro ? 1.0 : 0.8, MascotRim.Thickness(kind, theme));
        }
    }

    [Fact]
    public void ACatHasOneSharedThicknessOnMatcha()
    {
        // Kuro's dark body is the contrast on the light ground, so he keeps the normal outline there (a thick one made a heavy ring).
        Assert.Equal(MascotRim.CatNormalThickness, MascotRim.CatThickness(MascotKind.Kuro, "Matcha"));
        Assert.Equal(1.5, MascotRim.CatThickness(MascotKind.Shiro, "Matcha"));
        Assert.Equal(MascotRim.CatThickness(MascotKind.Kuro, "Matcha"), MascotRim.Thickness(MascotKind.Kuro, "Matcha"));
        Assert.Equal(MascotRim.CatThickness(MascotKind.Shiro, "Matcha"), MascotRim.Thickness(MascotKind.Shiro, "Matcha"));
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Hojicha")]
    [InlineData("KuroNight")]
    public void ACatKeepsTheNormalOutlineOnDarkGrounds(string theme)
    {
        Assert.Equal(1.0, MascotRim.CatThickness(MascotKind.Kuro, theme));
        Assert.Equal(1.0, MascotRim.CatThickness(MascotKind.Shiro, theme));
    }

    [Fact]
    public void TheMochiKindsAreNotAffectedByTheCatRule()
    {
        Assert.Equal(1.5, MascotRim.Thickness(MascotKind.Mochi, "Matcha"));
        Assert.Equal(1.5, MascotRim.Thickness(MascotKind.HojichaMochi, "Matcha"));
        Assert.Equal(1.5, MascotRim.Thickness(MascotKind.MatchaMochi, "Matcha"));
        Assert.Equal(0.8, MascotRim.Thickness(MascotKind.Mochi, "Default"));
    }

    [Fact]
    public void NeutralRimsBecomeTheDarkGrey() => Assert.Equal("#70727E", MascotRim.OnLightGround("#B8B9C0"));

    [Theory]
    [InlineData("#B08A60")]   // Hojicha Mochi's tan edge
    [InlineData("#C0392B")]
    [InlineData("#D9B35C")]
    [InlineData("#F8C9C2")]
    public void ColouredRimsAreDarkenedEnough(string edge)
    {
        string dark = MascotRim.OnLightGround(edge);
        int r = Convert.ToInt32(dark.Substring(1, 2), 16), g = Convert.ToInt32(dark.Substring(3, 2), 16), b = Convert.ToInt32(dark.Substring(5, 2), 16);
        Assert.True(MascotTint.Luminance(r, g, b) <= MascotRim.MaxLuminance);
    }

    [Fact]
    public void AlreadyDarkRimsAreKept() => Assert.Equal("#365D2A", MascotRim.OnLightGround("#365D2A"));
}
