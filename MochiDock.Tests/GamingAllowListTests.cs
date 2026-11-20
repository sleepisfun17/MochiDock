using MochiDock.Core;

public class GamingAllowListTests
{
    static GamingAllowEntry Wa(string key, string label = "") => new() { Kind = "WhatsApp", Key = key, Label = label };
    static GamingAllowEntry Dc(string key) => new() { Kind = "Discord", Key = key, Label = key };

    [Fact]
    public void WhatsAppMatchesTheChatLikeTheMutedList()
    {
        var entries = new[] { Wa("628123@s.whatsapp.net"), Wa("120363@g.us"), Wa("dev-1|555@g.us") };
        Assert.True(GamingAllowList.AllowsWhatsApp(entries, "628123@s.whatsapp.net", "dev-2"));
        Assert.True(GamingAllowList.AllowsWhatsApp(new[] { Wa("628123") }, "628123@s.whatsapp.net", "dev-1"));   // an entry saved as a number
        Assert.True(GamingAllowList.AllowsWhatsApp(entries, "120363@g.us", ""));
        Assert.True(GamingAllowList.AllowsWhatsApp(entries, "555@g.us", "dev-1"));             // listed for this account
        Assert.False(GamingAllowList.AllowsWhatsApp(entries, "555@g.us", "dev-2"));            // listed for another account only
        Assert.False(GamingAllowList.AllowsWhatsApp(entries, "999@s.whatsapp.net", "dev-1"));
        Assert.False(GamingAllowList.AllowsWhatsApp(entries, "", "dev-1"));
        Assert.False(GamingAllowList.AllowsWhatsApp(null, "628123@s.whatsapp.net", "dev-1"));
    }

    [Fact]
    public void DiscordMatchesTheSenderIgnoringCaseAndSpaces()
    {
        var entries = new[] { Dc("Teammate One"), Wa("Teammate One") };
        Assert.True(GamingAllowList.AllowsDiscord(entries, " teammate one "));
        Assert.False(GamingAllowList.AllowsDiscord(entries, "Someone Else"));
        Assert.False(GamingAllowList.AllowsDiscord(new[] { Wa("Teammate One") }, "Teammate One"));   // a WhatsApp entry does not allow Discord
        Assert.False(GamingAllowList.AllowsDiscord(entries, ""));
    }

    [Fact]
    public void KindsDoNotCross()
    {
        var entries = new[] { Dc("628123@s.whatsapp.net") };
        Assert.False(GamingAllowList.AllowsWhatsApp(entries, "628123@s.whatsapp.net", ""));
    }

    [Fact]
    public void AddSkipsDuplicatesAndEmptyKeys()
    {
        var list = GamingAllowList.Add(new List<GamingAllowEntry>(), Wa("120363@g.us", "Squad"));
        list = GamingAllowList.Add(list, Wa("120363@G.US", "Squad again"));
        list = GamingAllowList.Add(list, Dc("120363@g.us"));        // same key, other kind: allowed
        list = GamingAllowList.Add(list, Wa("  "));
        Assert.Equal(2, list.Count);
        Assert.Equal("Squad", list[0].Label);
    }

    [Theory]
    [InlineData("Ana", "see you at 8", "Ana: see you at 8")]
    [InlineData("Ana", "line one\nline two", "Ana: line one line two")]
    [InlineData("", "hello", "hello")]
    [InlineData("Ana", "", "Ana")]
    public void CapsuleTextIsOneShortLine(string sender, string message, string expected) =>
        Assert.Equal(expected, GamingAllowList.CapsuleText(sender, message));

    [Fact]
    public void CapsuleTextIsTrimmed()
    {
        string text = GamingAllowList.CapsuleText("Ana", new string('x', 200), 30);
        Assert.Equal(30, text.Length);
        Assert.EndsWith("…", text);
    }
}
