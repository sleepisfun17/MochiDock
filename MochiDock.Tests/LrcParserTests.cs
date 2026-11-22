using MochiDock.Services;

public class LrcParserTests
{
    [Fact]
    public void MultiTimestampLinesRepeatInTimeOrder()
    {
        var lines = LrclibService.ParseLrc("[ar:Someone]\n[00:10.50][01:00.00]chorus\n[00:05]verse\n[00:20.123]bridge");
        Assert.Equal(new[] { "verse", "chorus", "bridge", "chorus" }, lines.Select(l => l.Text));
        Assert.Equal(new[] { 5.0, 10.5, 20.123, 60.0 }, lines.Select(l => Math.Round(l.Timestamp.TotalSeconds, 3)));
    }

    [Fact]
    public void OffsetTagMovesLinesEarlierAndNeverBelowZero()
    {
        var lines = LrclibService.ParseLrc("[offset:+500]\n[00:00.20]a\n[00:02.00]b");
        Assert.Equal(0, lines[0].Timestamp.TotalSeconds, 3);
        Assert.Equal(1.5, lines[1].Timestamp.TotalSeconds, 3);
        var later = LrclibService.ParseLrc("[offset:-250]\n[00:01.00]a");
        Assert.Equal(1.25, later[0].Timestamp.TotalSeconds, 3);
    }

    [Fact]
    public void KeepsPausesDropsWordTagsAndUntimedLines()
    {
        var lines = LrclibService.ParseLrc("untimed\r\n[00:01.00]<00:01.00>hello <00:01.50>world\r\n[00:03.00]\r\n[00:04:50]colon fraction [live]");
        Assert.Equal(new[] { "hello world", "", "colon fraction [live]" }, lines.Select(l => l.Text));
        Assert.Equal(4.5, lines[2].Timestamp.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("Lemon (Official Video)", "Kenshi Yonezu", "Lemon", "Kenshi Yonezu")]
    [InlineData("Yoru ni Kakeru - Remastered 2021", "YOASOBI", "Yoru ni Kakeru", "YOASOBI")]
    [InlineData("Kenshi Yonezu - IRIS OUT [MV]", "Kenshi Yonezu - Topic", "IRIS OUT", "Kenshi Yonezu")]
    [InlineData("Song feat. Guest", "Main & Other", "Song", "Main")]
    [InlineData("Song (Reprise)", "Artist", "Song (Reprise)", "Artist")]
    public void NormalizeQueryRemovesPackaging(string title, string artist, string wantTitle, string wantArtist)
    {
        var (t, a) = LrclibService.NormalizeQuery(title, artist);
        Assert.Equal(wantTitle, t);
        Assert.Equal(wantArtist, a);
    }

    [Fact]
    public void MatchScoreAcceptsTheSameSongAndRejectsAnother()
    {
        Assert.True(LrclibService.MatchScore("Lemon", "Kenshi Yonezu", 255, "lemon", "米津玄師, Kenshi Yonezu", 256, true) >= LrclibService.MinScore);
        var other = LrclibService.MatchScore("Lemon", "Kenshi Yonezu", 255, "Flamingo", "Kenshi Yonezu", 200, true);
        Assert.True(other is null || other < LrclibService.MinScore);
    }

    [Fact]
    public void OverflowingMinutesTagIsIgnoredWithoutThrowing()
    {
        var lines = LrclibService.ParseLrc("[99999999999:00]line\n[00:05.00]valid");
        Assert.Single(lines);
        Assert.Equal("valid", lines[0].Text);
    }
}
