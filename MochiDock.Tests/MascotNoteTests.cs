using MochiDock.Core;

public class MascotNoteTests
{
    // The note floats outside the body, so it is read against the capsule ground (non-text contrast, 3:1).
    [Theory]
    [InlineData("Default")]
    [InlineData("Hojicha")]
    [InlineData("KuroNight")]
    [InlineData("Matcha")]
    public void TheNoteStandsOutOnEveryThemeGround(string theme)
    {
        string ink = MascotNote.InkFor(theme), ground = MascotNote.GroundFor(theme);
        Assert.True(MascotBadge.Contrast(ink, ground) >= 3, $"{ink} on {ground} ({theme}) is {MascotBadge.Contrast(ink, ground):N2}:1");
    }

    [Fact]
    public void TheLightGroundGetsTheDeepViolet()
    {
        Assert.Equal(MascotNote.OnLight, MascotNote.InkFor("Matcha"));
        Assert.Equal(MascotNote.OnDark, MascotNote.InkFor("KuroNight"));
    }
}

public class MascotBadgeRingTests
{
    // Both Mochi and the cats use the unified 3D charm; its outer rim must stand out against every theme ground.
    [Theory]
    [InlineData("Default")]
    [InlineData("Hojicha")]
    [InlineData("KuroNight")]
    [InlineData("Matcha")]
    public void TheOutlineStandsOutOnEveryGround(string theme)
    {
        string outline = MascotBadge.RingFor(theme), ground = MascotNote.GroundFor(theme);
        Assert.True(MascotBadge.Contrast(outline, ground) >= 3, $"{outline} on {ground} is {MascotBadge.Contrast(outline, ground):N2}:1");
    }
}
