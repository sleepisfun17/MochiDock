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
        Assert.Equal(needs ? 1.5 : 0.8, MascotRim.Thickness(kind, "Matcha"));
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
            Assert.Equal(0.8, MascotRim.Thickness(kind, theme));
        }
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
