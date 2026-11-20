using MochiDock.Core;

public class GameTrendTests
{
    static GameSessionRecord Session(string game, double avg, double low, double? ping = null) =>
        new() { Game = game, AverageFps = avg, Low1Fps = low, PingAverage = ping };

    [Fact]
    public void NeedsThreeEarlierSessionsOfTheSameGame()
    {
        var current = Session("Valorant", 200, 120);
        var two = new[] { Session("Valorant", 190, 110), Session("Valorant", 190, 110), Session("PUBG", 90, 60) };
        Assert.Null(GameTrend.Compare(current, two));
        var three = two.Append(Session("valorant ", 190, 110));
        Assert.Equal(3, GameTrend.Compare(current, three)!.Sessions);       // case and spaces do not matter
    }

    [Fact]
    public void ComparesWithTheMedianNotTheMean()
    {
        var earlier = new[] { Session("G", 100, 60), Session("G", 100, 60), Session("G", 100, 60), Session("G", 10, 5), Session("G", 400, 300) };
        var trend = GameTrend.Compare(Session("G", 112, 70), earlier)!;
        Assert.Equal(12, trend.AverageFpsDelta, 0.001);      // median 100, the odd sessions do not move it
        Assert.Equal(10, trend.Low1Delta, 0.001);
    }

    [Fact]
    public void UsesOnlyTheNewestTwentyAndSkipsTheSessionItself()
    {
        var current = Session("G", 50, 30);
        var earlier = new List<GameSessionRecord> { current };
        for (int i = 0; i < 20; i++) earlier.Add(Session("G", 100, 60));
        for (int i = 0; i < 30; i++) earlier.Add(Session("G", 10, 5));       // older than the newest twenty
        var trend = GameTrend.Compare(current, earlier)!;
        Assert.Equal(20, trend.Sessions);
        Assert.Equal(-50, trend.AverageFpsDelta, 0.001);
    }

    [Fact]
    public void PingNeedsEnoughEarlierPingNumbers()
    {
        var earlier = new[] { Session("G", 100, 60, 20), Session("G", 100, 60, 24), Session("G", 100, 60), Session("G", 100, 60) };
        Assert.Null(GameTrend.Compare(Session("G", 100, 60, 30), earlier)!.PingDelta);      // only two earlier pings
        var withPing = earlier.Append(Session("G", 100, 60, 22));
        Assert.Equal(8, GameTrend.Compare(Session("G", 100, 60, 30), withPing)!.PingDelta!.Value, 0.001);   // median 22
        Assert.Null(GameTrend.Compare(Session("G", 100, 60), withPing)!.PingDelta);         // this session has no ping
    }

    [Theory]
    [InlineData(206, 123, null, "vs your usual (8 sessions): avg +6 FPS, 1% low +3")]
    [InlineData(197, 117, null, "vs your usual (8 sessions): avg -3 FPS, 1% low -3")]
    [InlineData(200.4, 120.2, null, "About your usual (8 sessions)")]
    [InlineData(200, 120, 12.0, "vs your usual (8 sessions): ping +12 ms")]
    [InlineData(206, 123, -5.0, "vs your usual (8 sessions): avg +6 FPS, 1% low +3, ping -5 ms")]
    [InlineData(200, 120, 2.0, "About your usual (8 sessions)")]
    public void LineWording(double avgDelta, double lowDelta, double? ping, string expected) =>
        Assert.Equal(expected, GameTrend.Line(new GameTrendResult(8, avgDelta - 200, lowDelta - 120, ping)));
}
