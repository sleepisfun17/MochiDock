using MochiDock.Core;
using Xunit;

public class MentionResolverTests
{
    [Fact]
    public void Replace_SwapsKnownNumbersAndKeepsUnknownOnes()
    {
        string result = MentionResolver.Replace("hi @6281111111 and @6282222222!", n => n == "6281111111" ? "Budi" : null);
        Assert.Equal("hi @Budi and @6282222222!", result);
    }

    [Theory]
    [InlineData("6281111111@s.whatsapp.net", "6281111111")]
    [InlineData("6281111111:12@s.whatsapp.net", "6281111111")]
    [InlineData("123456789012@lid", "123456789012")]
    [InlineData("120363000000000001@g.us", "120363000000000001")]
    [InlineData("someone@example.com", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NumberOf_ReturnsOnlyPlainNumbers(string? jid, string expected) =>
        Assert.Equal(expected, MentionResolver.NumberOf(jid));

    [Fact]
    public void Replace_IgnoresShortNumbersAndEmails()
    {
        Assert.Equal("@123 a@b.com", MentionResolver.Replace("@123 a@b.com", _ => "X"));
        Assert.False(MentionResolver.HasMention("no mention here"));
    }
}
