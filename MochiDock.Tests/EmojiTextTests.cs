using MochiDock.Core;

public class EmojiTextTests
{
    private static string[] Keys(string text) => EmojiText.Split(text).Where(r => r.IsEmoji).Select(r => r.Key!).ToArray();

    [Fact]
    public void PlainTextIsOneRunAndEmptyIsNone()
    {
        var runs = EmojiText.Split("hello 123 # * (c) 2024");
        Assert.Single(runs);
        Assert.False(runs[0].IsEmoji);
        Assert.Empty(EmojiText.Split(""));
        Assert.Empty(EmojiText.Split(null));
        Assert.False(EmojiText.HasEmoji("just words"));
    }

    [Fact]
    public void AnEmojiInTextSplitsIntoThreeRuns()
    {
        var runs = EmojiText.Split("hi 😀!");
        Assert.Equal(new[] { "hi ", "😀", "!" }, runs.Select(r => r.Text));
        Assert.Equal(new bool[] { false, true, false }, runs.Select(r => r.IsEmoji));
        Assert.Equal("1f600", runs[1].Key);
        Assert.True(EmojiText.HasEmoji("hi 😀!"));
    }

    [Fact]
    public void NeighbouringEmojiAreSeparateRuns() => Assert.Equal(new[] { "1f600", "1f602" }, Keys("😀😂"));

    [Theory]
    [InlineData("👍🏽", "1f44d-1f3fd")]                                                    // a skin tone
    [InlineData("👨‍👩‍👧", "1f468-200d-1f469-200d-1f467")]                                // a family (joiners)
    [InlineData("👩‍❤️‍👨", "1f469-200d-2764-fe0f-200d-1f468")]                            // joiners keep U+FE0F, like the picture names
    [InlineData("🇯🇵", "1f1ef-1f1f5")]                                                    // a flag
    [InlineData("1️⃣", "31-20e3")]                                                          // a keycap
    [InlineData("❤️", "2764")]                                                             // a symbol made emoji by U+FE0F
    [InlineData("©️", "a9")]
    [InlineData("☀️", "2600")]
    [InlineData("🏴󠁧󠁢󠁥󠁮󠁧󠁿", "1f3f4-e0067-e0062-e0065-e006e-e0067-e007f")]                 // England: a tag sequence
    [InlineData("✨", "2728")]                                                              // emoji by default, no U+FE0F needed
    public void SequencesGetTheirPictureName(string text, string key) => Assert.Equal(new[] { key }, Keys(text));

    [Theory]
    [InlineData("❤")]          // text presentation of a symbol
    [InlineData("©")]
    [InlineData("™")]
    [InlineData("1")]
    [InlineData("#")]
    [InlineData("🇯")]         // one regional indicator alone
    [InlineData("é")]
    [InlineData("日本語")]
    public void SymbolsWithoutAnEmojiMarkStayText(string text) => Assert.Empty(Keys(text));

    [Fact]
    public void ADanglingJoinerDoesNotSwallowText()
    {
        var runs = EmojiText.Split("👨‍x");
        Assert.Equal("👨", runs[0].Text);
        Assert.Equal("‍x", runs[1].Text);
    }

    [Fact]
    public void TheRunsAlwaysAddUpToTheOriginal()
    {
        foreach (string text in new[] { "a😀b", "\uD83D lone high", "lone low \uDE00 x", "👨‍👩‍👧 family 🇯🇵 flag 1️⃣", "x️y", "tail 👍" })
            Assert.Equal(text, string.Concat(EmojiText.Split(text).Select(r => r.Text)));
    }

    [Fact]
    public void AVeryLongMessageIsSplitQuickly()
    {
        string text = string.Concat(Enumerable.Repeat("word 😀 ", 5000));
        Assert.Equal(5000, Keys(text).Length);
    }
}
