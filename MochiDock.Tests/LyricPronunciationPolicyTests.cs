using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class LyricPronunciationPolicyTests
{
    [Theory]
    [InlineData("I love you baby")]
    [InlineData("Kimi wo aishiteru")]
    [InlineData("ＬＯＶＥ")]
    [InlineData("123!!")]
    [InlineData("♪")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Tōkyō, ne?")]
    public void LinesWithoutKanaHanOrHangulNeedNoReading(string line) => Assert.False(LyricPronunciationPolicy.NeedsReading(line));

    [Theory]
    [InlineData("君が好き")]
    [InlineData("あいしてる")]
    [InlineData("カタカナ")]
    [InlineData("사랑해")]
    [InlineData("I love you 君が好き")]
    [InlineData("ﾊﾛｰ")]
    [InlineData("々")]
    public void LinesWithKanaHanOrHangulNeedAReading(string line) => Assert.True(LyricPronunciationPolicy.NeedsReading(line));

    [Fact]
    public void NullNeedsNoReading() => Assert.False(LyricPronunciationPolicy.NeedsReading(null));

    [Theory]
    [InlineData("Kimi wo aishiteru", "kimi wo aishiteru")]
    [InlineData("Tōkyō!", "tokyo")]
    [InlineData("ＬＯＶＥ", "love")]
    [InlineData("Wait... for me?", "wait for me")]
    [InlineData("「Hello」、world。", "hello world")]
    public void ARepeatedReadingIsRedundant(string text, string reading) => Assert.True(LyricPronunciationPolicy.IsRedundant(text, reading));

    [Theory]
    [InlineData("君が好き", "kimi ga suki")]
    [InlineData("I love you 君が好き", "I love you kimi ga suki")]
    [InlineData("hello", "")]
    public void ARealReadingIsNotRedundant(string text, string reading) => Assert.False(LyricPronunciationPolicy.IsRedundant(text, reading));
}
