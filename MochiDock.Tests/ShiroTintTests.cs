using MochiDock.Core;

public class ShiroTintTests
{
    private static readonly string[] Neutral = { "#EDEDEF", "#C4C5CA", "#B8B9C0", "#73FF7896" };
    private static readonly string[] Angry = { "#F8C9C2", "#EFA79C", "#C0392B", "#99E24B4A" };
    private static readonly string[] Electric = { "#FFF4C2", "#F5D96B", "#C9A227", "#66FFB347" };

    [Theory]
    [InlineData("Default")]
    [InlineData("Hojicha")]
    [InlineData("KuroNight")]
    [InlineData("Matcha")]
    public void NeutralFacesAreWhiteOnEveryTheme(string theme)
    {
        var s = ShiroTint.For(Neutral, theme);
        Assert.Equal(ShiroTint.WhiteTop, s.Top);
        Assert.Equal(ShiroTint.WhiteBottom, s.Bottom);
        Assert.Equal("#73FF7896", s.Cheek);
    }

    [Fact]
    public void TheRimIsDarkerOnMatcha()
    {
        Assert.Equal("#B8B9C0", ShiroTint.For(Neutral, "Default").Edge);
        Assert.Equal("#70727E", ShiroTint.For(Neutral, "Matcha").Edge);
    }

    [Fact]
    public void ColouredFacesKeepTheirOwnColour()
    {
        var s = ShiroTint.For(Angry, "Default");
        Assert.Equal("#F8C9C2", s.Top);
        Assert.Equal("#C0392B", s.Edge);
    }

    private static readonly string[] Victory = { "#FCEFC4", "#F4D98A", "#D9A21C", "#99FF7896" };
    private static readonly string[] Cream = { "#FFF6E0", "#F5E2B8", "#D9B35C", "#80FF7896" };   // Surprised, Sunny
    private static readonly string[] Peach = { "#FBEBD3", "#F2D2B0", "#C98A5B", "#80FF7896" };   // orange, not yellow

    [Theory]
    [InlineData("Default")]
    [InlineData("Hojicha")]
    [InlineData("KuroNight")]
    public void YellowFacesKeepTheWhiteBodyWithTheGoldInTheRim(string theme)
    {
        foreach (var face in new[] { Victory, Cream })
        {
            var s = ShiroTint.For(face, theme);
            Assert.Equal(ShiroTint.WhiteTop, s.Top);
            Assert.Equal(ShiroTint.WhiteBottom, s.Bottom);
            Assert.Equal(face[2], s.Edge);
            Assert.Equal(face[3], s.Cheek);
        }
    }

    [Fact]
    public void AYellowFaceOnMatchaGetsTheDarkGoldRim()
    {
        var s = ShiroTint.For(Victory, "Matcha");
        Assert.Equal(ShiroTint.WhiteTop, s.Top);
        Assert.Equal(ShiroTint.GoldRimOnMatcha, s.Edge);
    }

    [Fact]
    public void OrangeFacesAreNotTreatedAsYellow()
    {
        var s = ShiroTint.For(Peach, "Default");
        Assert.Equal("#FBEBD3", s.Top);
        Assert.Equal("#C98A5B", s.Edge);
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Hojicha")]
    [InlineData("KuroNight")]
    [InlineData("Matcha")]
    public void ElectricNeverTurnsAnyCatYellow(string theme)
    {
        // Shiro: the Electric face is forced to the white body.
        var s = ShiroTint.For(Electric, theme, forceNeutral: true);
        Assert.Equal(ShiroTint.WhiteTop, s.Top);
        Assert.Equal(ShiroTint.WhiteBottom, s.Bottom);
        Assert.NotEqual("#C9A227", s.Edge);
        // Kuro: charcoal body, whatever the face.
        var k = MascotTint.KuroNight(Electric);
        Assert.Equal(MascotTint.CharcoalTop, k.BodyTop);
        Assert.Equal(MascotTint.CharcoalBottom, k.BodyBottom);
    }
}
