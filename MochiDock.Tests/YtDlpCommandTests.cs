using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MochiDock.Core;
using Xunit;

public class YtDlpCommandTests
{
    [Theory]
    [InlineData("https://example.com/watch?v=1", true)]
    [InlineData("  http://example.com/a b  ", true)]
    [InlineData("ftp://example.com/x", false)]
    [InlineData("file:///C:/x", false)]
    [InlineData("--exec calc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseUrl_AcceptsOnlyHttpUrls(string? input, bool expected) =>
        Assert.Equal(expected, YtDlpCommand.TryParseUrl(input, out _));

    [Fact]
    public void TryParseUrl_QuoteInUrlIsEscapedNotInjected()
    {
        Assert.True(YtDlpCommand.TryParseUrl("https://example.com/a\" --exec calc \"", out var url));
        Assert.DoesNotContain("\"", url);
        Assert.DoesNotContain(" ", url);
    }

    [Fact]
    public void BuildArguments_UrlIsLastSingleArgumentAfterSeparator()
    {
        var args = YtDlpCommand.BuildArguments("https://example.com/x", @"C:\out dir", audioOnly: false);
        Assert.Equal("https://example.com/x", args[^1]);
        Assert.Equal("--", args[^2]);
        Assert.Contains(args, a => a.StartsWith(@"C:\out dir"));
    }

    [Fact]
    public void BuildArguments_AudioUsesMp3Extraction() =>
        Assert.Contains("mp3", YtDlpCommand.BuildArguments("https://e.com", "d", true));

    [Fact]
    public void FindExpectedHash_ReadsMatchingLine()
    {
        var hash = new string('a', 64);
        var sums = $"{new string('b', 64)}  yt-dlp\n{hash} *yt-dlp.exe\n";
        Assert.Equal(hash, YtDlpCommand.FindExpectedHash(sums, "yt-dlp.exe"));
        Assert.Null(YtDlpCommand.FindExpectedHash(sums, "missing.exe"));
    }

    [Fact]
    public void HashMatches_DetectsTamperingAndMissingHash()
    {
        var data = Encoding.UTF8.GetBytes("binary");
        var hex = System.Convert.ToHexString(SHA256.HashData(data));
        Assert.True(YtDlpCommand.HashMatches(data, hex.ToLowerInvariant()));
        Assert.False(YtDlpCommand.HashMatches(data.Append((byte)1).ToArray(), hex));
        Assert.False(YtDlpCommand.HashMatches(data, null));
    }
}
