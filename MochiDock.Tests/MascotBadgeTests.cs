using MochiDock.Core;

public class MascotBadgeTests
{
    // The face-by-face table: the faces with a badge and its glyph. Every other face has none.
    private static readonly Dictionary<MascotMood, string> Expected = new()
    {
        [MascotMood.Alert] = "exclaim", [MascotMood.Excited] = "exclaim",
        [MascotMood.Sleeping] = "z", [MascotMood.Yawning] = "moon",
        [MascotMood.Coffee] = "cup", [MascotMood.Praying] = "bell", [MascotMood.BreakTime] = "pause",
        [MascotMood.Victory] = "trophy", [MascotMood.Angry] = "clock", [MascotMood.Drained] = "battery",
        [MascotMood.Money] = "up", [MascotMood.MoneyDown] = "down",
        [MascotMood.Sunny] = "sun", [MascotMood.Rainy] = "umbrella", [MascotMood.Snowy] = "snow", [MascotMood.Cloudy] = "cloud",
        [MascotMood.Studying] = "timer", [MascotMood.Proud] = "tick",
    };

    [Fact]
    public void EveryMoodHasItsBadgeFromTheTableOrNone()
    {
        foreach (var mood in Enum.GetValues<MascotMood>())
        {
            var badge = MascotBadge.For(mood);
            if (Expected.TryGetValue(mood, out var glyph)) Assert.Equal(glyph, badge?.Glyph);
            else Assert.Null(badge);
        }
    }

    [Fact]
    public void TheFacesWithoutABadgeAreTheOnesTheEyesTell()
    {
        foreach (var mood in new[] { MascotMood.Idle, MascotMood.Waiting, MascotMood.Music, MascotMood.Focused, MascotMood.Happy, MascotMood.Hot,
                     MascotMood.Freezing, MascotMood.Dizzy, MascotMood.Defeat, MascotMood.Worried, MascotMood.Surprised, MascotMood.Cheering,
                     MascotMood.Bored, MascotMood.Waking, MascotMood.Electric, MascotMood.Birthday, MascotMood.Meeting })
            Assert.Null(MascotBadge.For(mood));
    }

    // The cats wear no headset: the faces the headset told get a badge of their own, and the Mochi kinds have none there.
    [Theory]
    [InlineData(MascotKind.Kuro)]
    [InlineData(MascotKind.Shiro)]
    public void CatsGetBadgesForMusicGamingAndMeetings(MascotKind kind)
    {
        Assert.Equal(("note", "#6E4BB8"), Pair(MascotBadge.For(MascotMood.Music, kind)));
        Assert.Equal(("controller", "#0F6E56"), Pair(MascotBadge.For(MascotMood.Focused, kind)));
        Assert.Equal(("controller", "#0F6E56"), Pair(MascotBadge.For(MascotMood.Worried, kind)));
        Assert.Equal(("mic", "#3F4C66"), Pair(MascotBadge.For(MascotMood.Meeting, kind)));
    }

    [Theory]
    [InlineData(MascotKind.Mochi)]
    [InlineData(MascotKind.MatchaMochi)]
    [InlineData(MascotKind.HojichaMochi)]
    public void TheMochiKindsKeepTheHeadsetAndHaveNoBadgeThere(MascotKind kind)
    {
        foreach (var mood in new[] { MascotMood.Music, MascotMood.Focused, MascotMood.Worried, MascotMood.Meeting })
            Assert.Null(MascotBadge.For(mood, kind));
    }

    [Fact]
    public void EveryOtherFaceHasTheSameBadgeOnEveryMascot()
    {
        foreach (var mood in Enum.GetValues<MascotMood>())
        {
            if (mood is MascotMood.Music or MascotMood.Focused or MascotMood.Worried or MascotMood.Meeting) continue;
            foreach (var kind in Enum.GetValues<MascotKind>()) Assert.Equal(MascotBadge.For(mood), MascotBadge.For(mood, kind));
        }
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Hojicha")]
    [InlineData("KuroNight")]
    [InlineData("Matcha")]
    public void TheCatBadgesAreReadableAndSeparatedFromTheirRing(string theme)
    {
        foreach (var mood in new[] { MascotMood.Music, MascotMood.Focused, MascotMood.Worried, MascotMood.Meeting })
        {
            var b = MascotBadge.For(mood, MascotKind.Kuro)!.Value;
            Assert.True(MascotBadge.Contrast(b.Ink, b.Fill) >= 4.5, $"{mood}: glyph on {b.Fill} is {MascotBadge.Contrast(b.Ink, b.Fill):N2}:1");
            string ring = MascotBadge.RingFor(theme, b.Fill);
            Assert.True(MascotBadge.Contrast(ring, b.Fill) >= 3, $"{mood} on {theme}: ring {ring} against {b.Fill} is {MascotBadge.Contrast(ring, b.Fill):N2}:1");
        }
    }

    private static (string, string) Pair(BadgeSpec? b) => (b!.Value.Glyph, b.Value.Fill);

    [Fact]
    public void NewMessageAndNoticeShareTheRedExclamationMark()
    {
        var notice = MascotBadge.For(MascotMood.Alert);
        var message = MascotBadge.For(MascotMood.Excited);
        Assert.Equal(notice, message);
        Assert.Equal("exclaim", message!.Value.Glyph);
        Assert.Equal("#D13438", message.Value.Fill);
    }

    [Fact]
    public void TheGlyphIsReadableOnItsDisc()
    {
        foreach (var mood in Expected.Keys)
        {
            var b = MascotBadge.For(mood)!.Value;
            Assert.True(MascotBadge.Contrast(b.Ink, b.Fill) >= 4.5, $"{mood}: {b.Ink} on {b.Fill} is {MascotBadge.Contrast(b.Ink, b.Fill):N2}:1");
        }
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Hojicha")]
    [InlineData("KuroNight")]
    [InlineData("Matcha")]
    public void TheRingSeparatesTheDiscOnEveryTheme(string theme)
    {
        foreach (var mood in Expected.Keys)
        {
            var b = MascotBadge.For(mood)!.Value;
            string ring = MascotBadge.RingFor(theme, b.Fill);
            Assert.True(MascotBadge.Contrast(ring, b.Fill) >= 3, $"{mood} on {theme}: ring {ring} against {b.Fill} is {MascotBadge.Contrast(ring, b.Fill):N2}:1");
        }
    }

    [Fact]
    public void TheRingIsWhiteOnDarkGroundsAndDarkOnMatcha()
    {
        Assert.Equal("#FFFFFF", MascotBadge.RingFor("Default"));
        Assert.Equal("#FFFFFF", MascotBadge.RingFor("Hojicha"));
        Assert.Equal("#FFFFFF", MascotBadge.RingFor("KuroNight"));
        Assert.Equal(MascotBadge.DarkRing, MascotBadge.RingFor("Matcha"));
        Assert.Equal("#FFFFFF", MascotBadge.RingFor("unknown"));
    }

    [Fact]
    public void ADiscTooDarkForTheDarkRingGetsTheWhiteOneOnMatcha()
    {
        // Navy (Sleeping, Up late) would be about 1.7:1 against the dark ring.
        Assert.Equal("#FFFFFF", MascotBadge.RingFor("Matcha", "#2F3E8F"));
        Assert.Equal(MascotBadge.DarkRing, MascotBadge.RingFor("Matcha", "#D13438"));
    }

    [Fact]
    public void NoBadgeIsYellow()
    {
        // No cat ever turns yellow, and the badge colours are never a yellow body either: gold and amber are dark enough to read as brown.
        foreach (var mood in Expected.Keys)
        {
            var b = MascotBadge.For(mood)!.Value;
            int r = Convert.ToInt32(b.Fill.Substring(1, 2), 16), g = Convert.ToInt32(b.Fill.Substring(3, 2), 16);
            Assert.True(MascotTint.Luminance(r, g, Convert.ToInt32(b.Fill.Substring(5, 2), 16)) < 0.2);
        }
    }
}
