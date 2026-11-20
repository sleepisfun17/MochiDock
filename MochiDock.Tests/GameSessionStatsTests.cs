using MochiDock.Core;

public class GameSessionStatsTests
{
    static GameSessionStats Constant(double fps, int frames)
    {
        var stats = new GameSessionStats();
        for (int i = 0; i < frames; i++) stats.Add(1000.0 / fps);
        return stats;
    }

    [Fact]
    public void ConstantSixtyFpsGivesSixtyEverywhere()
    {
        var r = Constant(60, 6000).Result()!;   // 100 seconds
        Assert.Equal(60, r.AverageFps, 0.5);
        Assert.Equal(60, r.Low1Fps, 1.0);
        Assert.Equal(60, r.Low01Fps, 1.0);
        Assert.Equal(60, r.MinFps, 1.0);
        Assert.Equal(60, r.MaxFps, 1.0);
        Assert.Equal(0, r.Stutters);
        Assert.Equal(100, r.Seconds, 0.5);
    }

    [Fact]
    public void LowsAverageTheSlowestFramesNotTheSingleWorstOne()
    {
        // 9,900 frames at 10 ms (100 FPS) plus 100 frames at 50 ms (20 FPS): the slowest 1% is exactly those 100 frames.
        var stats = new GameSessionStats();
        for (int i = 0; i < 9900; i++) stats.Add(10);
        for (int i = 0; i < 100; i++) stats.Add(50);
        var r = stats.Result()!;
        Assert.Equal(20, r.Low1Fps, 0.5);
        Assert.Equal(20, r.MinFps, 0.5);
        Assert.Equal(100, r.MaxFps, 0.5);
        // average FPS is frames / time: 10,000 frames over 9,900*10 + 100*50 = 104,000 ms
        Assert.Equal(10000 / 104.0, r.AverageFps, 0.5);
        Assert.Equal(100, r.Stutters);          // frames slower than twice the median (10 ms)
    }

    [Fact]
    public void AFastGamesNormalJitterIsNotAStutter()
    {
        // 600 FPS (1.7 ms median): frames of 4 ms are more than twice the median but nobody can see them. Only frames over 20 ms count.
        var stats = new GameSessionStats();
        for (int i = 0; i < 9000; i++) stats.Add(1.7);
        for (int i = 0; i < 900; i++) stats.Add(4);
        for (int i = 0; i < 7; i++) stats.Add(30);
        Assert.Equal(7, stats.Result(enforceMinimums: false)!.Stutters);
        Assert.False(GameSessionStats.IsStutter(4, 1.7));
        Assert.True(GameSessionStats.IsStutter(30, 1.7));
        Assert.False(GameSessionStats.IsStutter(18, 5));      // over twice the median but under the floor
    }

    [Fact]
    public void ZeroPointOnePercentLowIsTheWorstTenthOfAPercent()
    {
        // 10,000 frames: 9,990 at 10 ms, 10 at 100 ms. The worst 0.1% is those 10 frames (10 FPS); the worst 1% mixes in 90 fast ones.
        var stats = new GameSessionStats();
        for (int i = 0; i < 9990; i++) stats.Add(10);
        for (int i = 0; i < 10; i++) stats.Add(100);
        var r = stats.Result()!;
        Assert.Equal(10, r.Low01Fps, 0.5);
        double worstOnePercentMs = (10 * 100 + 90 * 10) / 100.0;
        Assert.Equal(1000 / worstOnePercentMs, r.Low1Fps, 0.5);
        Assert.True(r.Low01Fps < r.Low1Fps);
    }

    [Fact]
    public void PausesAndBadValuesAreNotFrames()
    {
        var stats = Constant(60, 6000);
        Assert.False(stats.Add(1000));       // a one-second gap: alt-tab or a loading screen
        Assert.False(stats.Add(5000));
        Assert.False(stats.Add(0));
        Assert.False(stats.Add(-3));
        Assert.False(stats.Add(double.NaN));
        Assert.False(stats.Add(double.PositiveInfinity));
        Assert.Equal(6000, stats.Frames);
        Assert.Equal(60, stats.Result()!.MinFps, 1.0);   // the pause did not drag the minimum to 1 FPS
    }

    [Fact]
    public void FramesSlowerThanTwoHundredMsStillCountWithTheirTrueTime()
    {
        var stats = new GameSessionStats();
        for (int i = 0; i < 5000; i++) stats.Add(16.7);
        for (int i = 0; i < 100; i++) stats.Add(400);       // overflow bucket
        var r = stats.Result()!;
        Assert.Equal(2.5, r.MinFps, 0.05);                  // 1000 / 400 ms
        Assert.Equal(100, r.Stutters);
        Assert.Equal(2.5, r.Low1Fps, 0.1);
    }

    [Theory]
    [InlineData(200, 60, false)]     // too few frames
    [InlineData(299, 30, false)]
    [InlineData(2000, 200, false)]   // under a minute (10 seconds)
    [InlineData(6000, 60, true)]
    public void ShortSessionsGiveNoResult(int frames, double fps, bool expected)
    {
        var stats = Constant(fps, frames);
        Assert.Equal(expected, stats.Result() != null);
    }

    [Fact]
    public void MinimumsCanBeSwitchedOffAndAnEmptySessionHasNoResult()
    {
        Assert.NotNull(Constant(60, 10).Result(enforceMinimums: false));
        Assert.Null(new GameSessionStats().Result(enforceMinimums: false));
    }

    [Fact]
    public void FormattingAndHistory()
    {
        var record = GameSessionRecord.From("Game", new DateTime(2026, 11, 14, 20, 0, 0),
            new GameSessionResult(100000, 7500, 142.4, 96.2, 70.6, 58.1, 240.0, 1));
        Assert.Equal("Avg 142 FPS · 1% low 96 · 0.1% low 71", GameSessionFormat.FpsLine(record));
        Assert.Equal("Min 58 · 1 stutter · 2h 05m", GameSessionFormat.DetailLine(record));
        Assert.Equal("5m 07s", GameSessionFormat.Duration(307));

        var history = new List<GameSessionRecord>();
        for (int i = 0; i < 60; i++) history = GameSessionFormat.AddToHistory(history, new GameSessionRecord { Game = "G" + i });
        Assert.Equal(GameSessionFormat.HistoryCap, history.Count);
        Assert.Equal("G59", history[0].Game);      // newest first
    }

    [Fact]
    public void HardwareAveragesLoadAndKeepsOnlyMeasuredPeakTemperature()
    {
        var hw = new GameSessionHardware();
        hw.Add(80, 70, true); hw.Add(60, 74, true); hw.Add(100, 99, false); hw.Add(double.NaN, 60, true);
        var record = new GameSessionRecord();
        hw.Apply(record);
        Assert.Equal(80, record.GpuAveragePercent!.Value, 0.01);
        Assert.Equal(74, record.GpuPeakTemp);                       // the estimated 99 is ignored
        Assert.Equal("GPU avg 80% · max 100% · max temp 74°", GameSessionFormat.HardwareLine(record));
    }

    [Fact]
    public void TooFewHardwareSamplesGiveNoNumbers()
    {
        var hw = new GameSessionHardware();
        hw.Add(50, 60, false); hw.Add(50, 60, false);
        var record = new GameSessionRecord();
        hw.Apply(record);
        Assert.Null(record.GpuAveragePercent);
        Assert.Null(record.GpuPeakTemp);
        Assert.Equal("", GameSessionFormat.HardwareLine(record));
    }

    [Fact]
    public void TheSessionKeepsTheCpuPeakLoadAndTheMeasuredPeakTemperature()
    {
        var hw = new GameSessionHardware();
        hw.Add(50, 60, true, cpuPercent: 30, cpuTemp: 55, cpuTempMeasured: true);
        hw.Add(60, 62, true, cpuPercent: 82, cpuTemp: 71, cpuTempMeasured: true);
        hw.Add(55, 61, true, cpuPercent: 40, cpuTemp: 66, cpuTempMeasured: true);
        var record = new GameSessionRecord();
        hw.Apply(record);
        Assert.Equal(82, record.CpuPeakPercent);
        Assert.Equal(71, record.CpuPeakTemp);
        Assert.Equal("CPU avg 51% · max 82% · max temp 71°", GameSessionFormat.CpuLine(record));
        Assert.Equal(new[] { "51%", "82%", "71°" }, GameSessionFormat.CpuStats(record).Select(s => s.Value));
    }

    [Fact]
    public void AnEstimatedCpuTemperatureIsNeverShownAsAPeak()
    {
        var hw = new GameSessionHardware();
        for (int i = 0; i < 5; i++) hw.Add(50, 60, true, cpuPercent: 40 + i, cpuTemp: 70, cpuTempMeasured: false);   // no sensor: an estimate from load
        var record = new GameSessionRecord();
        hw.Apply(record);
        Assert.Equal(44, record.CpuPeakPercent);
        Assert.Null(record.CpuPeakTemp);
        Assert.Equal("CPU avg 42% · max 44%", GameSessionFormat.CpuLine(record));
    }

    [Fact]
    public void TooFewCpuSamplesGiveNoCpuLineAndOldRecordsStillWork()
    {
        var hw = new GameSessionHardware();
        hw.Add(50, 60, true, cpuPercent: 90);
        var record = new GameSessionRecord();
        hw.Apply(record);
        Assert.Null(record.CpuPeakPercent);
        Assert.Equal("", GameSessionFormat.CpuLine(record));
        Assert.Equal("", GameSessionFormat.CpuLine(new GameSessionRecord()));   // a record saved before this existed
    }

    [Fact]
    public void TheGpuAndTheCpuReadTheSameWay()
    {
        var hw = new GameSessionHardware();
        hw.Add(50, 60, true, cpuPercent: 30, cpuTemp: 55, cpuTempMeasured: true);
        hw.Add(90, 88, true, cpuPercent: 60, cpuTemp: 71, cpuTempMeasured: true);
        hw.Add(70, 70, true, cpuPercent: 45, cpuTemp: 66, cpuTempMeasured: true);
        var record = new GameSessionRecord();
        hw.Apply(record);
        Assert.Equal("GPU avg 70% · max 90% · max temp 88°", GameSessionFormat.HardwareLine(record));
        Assert.Equal("CPU avg 45% · max 60% · max temp 71°", GameSessionFormat.CpuLine(record));
        var gpu = GameSessionFormat.GpuStats(record);
        Assert.Equal(new[] { "avg load", "max load", "max temp" }, gpu.Select(x => x.Caption));
        Assert.True(gpu[2].Warn);                                      // 88 degrees is flagged
        Assert.False(GameSessionFormat.CpuStats(record)[2].Warn);      // 71 is not
    }

    [Fact]
    public void TheSummaryStatsHaveShortCaptions()
    {
        var record = new GameSessionRecord { AverageFps = 142.4, Low1Fps = 96, Low01Fps = 71, MinFps = 58, Stutters = 1, Seconds = 125, PingAverage = 23, PingWorst = 112, PingSpikes = 2, PingLost = 1 };
        Assert.Equal(new[] { "142", "96", "71" }, GameSessionFormat.SpeedStats(record).Select(s => s.Value));
        Assert.Equal(new[] { "58", "1", "2m 05s" }, GameSessionFormat.FrameStats(record).Select(s => s.Value));
        Assert.Equal("stutter", GameSessionFormat.FrameStats(record)[1].Caption);
        var ping = GameSessionFormat.PingStats(record);
        Assert.Equal(new[] { "23 ms", "112 ms", "2", "1" }, ping.Select(s => s.Value));
        Assert.True(ping[3].Warn);
        Assert.Empty(GameSessionFormat.PingStats(new GameSessionRecord()));
        Assert.Empty(GameSessionFormat.GpuStats(new GameSessionRecord()));
    }
}
