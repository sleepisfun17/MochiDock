using MochiDock.Core;

public class GameSessionTimelineTests
{
    static GameSessionTimeline Constant(double fps, double seconds)
    {
        var timeline = new GameSessionTimeline();
        int frames = (int)(fps * seconds);
        for (int i = 0; i < frames; i++) timeline.AddFrame(1000.0 / fps);
        return timeline;
    }

    [Fact]
    public void ConstantSessionGivesOnePointPerTwoSeconds()
    {
        var points = Constant(60, 20).Points();
        Assert.InRange(points.Count, 9, 11);
        Assert.All(points, p => Assert.Equal(60, p.Fps, 1.0));
        Assert.All(points, p => Assert.Equal(1000.0 / 60, p.WorstFrameMs, 0.1));
        Assert.All(points, p => Assert.Null(p.PingMs));
    }

    [Fact]
    public void ASpikeShowsInTheWorstFrameOfItsBucketOnly()
    {
        var timeline = new GameSessionTimeline();
        for (int i = 0; i < 600; i++) timeline.AddFrame(i == 300 ? 250 : 10);
        var points = timeline.Points();
        Assert.Single(points, p => p.WorstFrameMs >= 250);
        Assert.Equal(250, points.Max(p => p.WorstFrameMs));
    }

    [Fact]
    public void LongSessionsMergeBucketsAndStayBounded()
    {
        // 3 hours at 100 FPS would be 5,400 buckets of 2 s; the timeline must stay within its fixed size.
        var timeline = new GameSessionTimeline();
        for (int i = 0; i < 1_080_000; i++) timeline.AddFrame(10);
        Assert.True(timeline.BucketCount <= GameSessionTimeline.MaxBuckets);
        Assert.True(timeline.BucketSeconds > GameSessionTimeline.StartWidthSeconds);
        var points = timeline.Points();
        Assert.True(points.Count <= GameSessionTimeline.MaxBuckets);
        Assert.All(points, p => Assert.Equal(100, p.Fps, 1.0));
    }

    [Fact]
    public void MergingKeepsTheWorstValues()
    {
        var timeline = new GameSessionTimeline();
        // One 400 ms hitch, then enough smooth play to force at least one merge.
        timeline.AddFrame(400);
        timeline.AddPing(180);
        for (int i = 0; i < 80_000; i++) timeline.AddFrame(10);
        Assert.True(timeline.BucketSeconds > GameSessionTimeline.StartWidthSeconds);
        var points = timeline.Points();
        Assert.Equal(400, points.Max(p => p.WorstFrameMs));
        Assert.Equal(180, points.Max(p => p.PingMs));
    }

    [Fact]
    public void PausesAndBadValuesAreNotFrames()
    {
        var timeline = new GameSessionTimeline();
        timeline.AddFrame(5000);
        timeline.AddFrame(double.NaN);
        timeline.AddFrame(-1);
        Assert.Empty(timeline.Points());
    }

    [Fact]
    public void PingsLandInTheBucketBeingFilledAndLostPingsAreMarked()
    {
        var timeline = new GameSessionTimeline();
        for (int i = 0; i < 100; i++) timeline.AddFrame(10);
        timeline.AddPing(24);
        timeline.AddPing(31);
        timeline.AddPing(null);
        for (int i = 0; i < 100; i++) timeline.AddFrame(10);
        var first = timeline.Points()[0];
        Assert.Equal(31, first.PingMs);
        Assert.True(first.PingLost);
    }

    [Fact]
    public void PingStatsNeedFiveAnswers()
    {
        var stats = new PingStats();
        for (int i = 0; i < 4; i++) stats.Add(20);
        stats.Add(null);
        Assert.Null(stats.Result());
        stats.Add(20);
        Assert.NotNull(stats.Result());
    }

    [Fact]
    public void PingAverageWorstJitterSpikesAndLoss()
    {
        var stats = new PingStats();
        foreach (var p in new double?[] { 20, 22, 20, 24, 120, 22, 20, null, 22, 20 }) stats.Add(p);
        var r = stats.Result()!;
        Assert.Equal((20 + 22 + 20 + 24 + 120 + 22 + 20 + 22 + 20) / 9.0, r.Average, 0.01);
        Assert.Equal(120, r.Worst);
        Assert.Equal(1, r.Spikes);          // 120 is over twice the median (22) and more than 30 ms above it
        Assert.Equal(1, r.Lost);
        double jitter = (2 + 2 + 4 + 96 + 98 + 2 + 2 + 2) / 8.0;   // the lost ping does not break the chain of answered pings
        Assert.Equal(jitter, r.Jitter, 0.01);
    }

    [Fact]
    public void ASteadyLowPingNeverSpikes()
    {
        var stats = new PingStats();
        foreach (var p in new double?[] { 4, 5, 4, 12, 5, 4, 5, 4 }) stats.Add(p);   // 12 is over 2x the median but only 7 ms above it
        Assert.Equal(0, stats.Result()!.Spikes);
    }

    [Theory]
    [InlineData("1.1.1.1", true)]
    [InlineData(" 8.8.8.8 ", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("game.example.com", true)]
    [InlineData("", false)]
    [InlineData("two words", false)]
    [InlineData("http://x.com/a", false)]
    [InlineData("host:80", false)]
    public void PingHostValidation(string text, bool expected) =>
        Assert.Equal(expected, SettingsValidation.TryNormalizePingHost(text, out _));

    [Fact]
    public void OnlyTheNewestSessionsKeepTheirGraphAndOldFilesStillLoad()
    {
        var history = new List<GameSessionRecord>();
        for (int i = 0; i < 14; i++)
            history = GameSessionFormat.AddToHistory(history, new GameSessionRecord { Game = "G" + i, Timeline = new() { new(), new() } });
        Assert.Equal(GameSessionFormat.TimelinesKept, history.Count(r => r.Timeline != null));
        Assert.NotNull(history[0].Timeline);
        Assert.Null(history[GameSessionFormat.TimelinesKept].Timeline);

        // A record written before ping and timeline existed has none of the new fields.
        var old = System.Text.Json.JsonSerializer.Deserialize<GameSessionRecord>("{\"Game\":\"Old\",\"AverageFps\":90}")!;
        Assert.Null(old.Timeline);
        Assert.Null(old.PingAverage);
        Assert.Equal("", GameSessionFormat.PingLine(old));
    }

    [Fact]
    public void PingLineAndTimelineRoundTrip()
    {
        var timeline = new GameSessionTimeline();
        for (int i = 0; i < 1000; i++) timeline.AddFrame(10);
        var stats = new PingStats();
        foreach (var p in new double?[] { 20, 22, 20, 24, 120, 22, null }) stats.Add(p);
        var record = new GameSessionRecord();
        record.ApplyTimeline(timeline);
        record.ApplyPing(stats.Result());
        Assert.Equal("Ping avg 38 · worst 120 · 1 spike · 1 lost", GameSessionFormat.PingLine(record));

        var json = System.Text.Json.JsonSerializer.Serialize(record);
        var back = System.Text.Json.JsonSerializer.Deserialize<GameSessionRecord>(json)!;
        Assert.Equal(record.Timeline!.Count, back.Timeline!.Count);
        Assert.Equal(record.TimelineBucketSeconds, back.TimelineBucketSeconds);
    }

    [Fact]
    public void GraphLayoutDoesNotMarkFastGamesJitterAsStutters()
    {
        var points = Enumerable.Range(0, 50).Select(i => new GameTimelinePoint { Fps = 600, WorstFrameMs = 5 }).ToList();   // 1.7 ms median, 5 ms worst
        Assert.Empty(GameGraphLayout.Layout(points, 260, 76)!.StutterX);
        points[10] = new GameTimelinePoint { Fps = 600, WorstFrameMs = 40 };
        Assert.Single(GameGraphLayout.Layout(points, 260, 76)!.StutterX);
    }

    [Fact]
    public void GraphLayoutScalesFpsPingAndMarksStutters()
    {
        var points = new List<GameTimelinePoint>
        {
            new() { Fps = 100, WorstFrameMs = 12, PingMs = 20 },
            new() { Fps = 50,  WorstFrameMs = 90, PingMs = null },     // a stutter: 90 ms is over twice the 10 ms median
            new() { Fps = 100, WorstFrameMs = 12, PingMs = 100 },
        };
        var shape = GameGraphLayout.Layout(points, 200, 80)!;
        Assert.Equal(3, shape.Fps.Count);
        Assert.Equal(0, shape.Fps[0].Y, 0.01);              // the highest FPS is at the top
        Assert.Equal(40, shape.Fps[1].Y, 0.01);             // half the FPS is half way down
        Assert.Equal(200, shape.Fps[2].X, 0.01);
        Assert.Equal(2, shape.Ping.Count);                  // the bucket without a ping is skipped
        Assert.Equal(0, shape.Ping[1].Y, 0.01);             // 100 ms is the scale's maximum
        Assert.Equal(new[] { 100.0 }, shape.StutterX);
        Assert.Null(GameGraphLayout.Layout(points.Take(1).ToList(), 200, 80));
        Assert.Null(GameGraphLayout.Layout(null, 200, 80));
    }
}
