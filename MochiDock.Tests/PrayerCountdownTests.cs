using MochiDock.Core;
public class PrayerCountdownTests {
 [Fact]public void WindowWorksOnStartupAndAcknowledgementOnlyDismissesCurrentPrayer(){var now=DateTimeOffset.UtcNow;var s=new PrayerCountdownState();Assert.False(s.Evaluate("a",now.AddMinutes(11),now));Assert.True(s.Evaluate("a",now.AddMinutes(7),now));s.Acknowledge();Assert.False(s.Evaluate("a",now.AddMinutes(7),now));Assert.True(s.Evaluate("b",now.AddMinutes(7),now));Assert.False(s.Evaluate("b",now,now));}
 [Theory][InlineData(600,10)][InlineData(421,8)][InlineData(420,7)][InlineData(1,1)][InlineData(0,0)]public void CountdownRoundsUp(int seconds,int minutes){var now=DateTimeOffset.UtcNow;Assert.Equal(minutes,PrayerCountdownState.Minutes(now.AddSeconds(seconds),now));}
}