using MochiDock.Core;
public class PlaybackClockTests {
 [Fact] public void SnapshotIncludesAgeAndRate(){var p=new PlaybackSnapshot(10,100,8,true,2);Assert.Equal(14,p.At(10));Assert.Equal(100,p.At(100));}
 [Fact] public void PausedPositionIsFrozen(){Assert.Equal(10,new PlaybackSnapshot(10,100,0,false).At(900));}
 [Theory][InlineData(-1)][InlineData(3601)][InlineData(double.NaN)]public void InvalidSnapshotAgeUsesReceiptTime(double age){Assert.Equal(0,PlaybackSnapshot.ValidAge(age));}
 [Fact] public void LyricClockDoesNotReverseForJitterButTakesSeeks(){var c=new LyricClock();Assert.Equal(10,c.Tick(0,10,true,1));var p=c.Tick(.1,9.9,true,1);Assert.True(p>=10);Assert.Equal(2,c.Tick(.2,2,true,1));c.Reset();Assert.Equal(0,c.Tick(.3,0,true,1));}
 [Fact] public void SeekSuppressesOldReportsOnlyDuringConfirmationWindow(){var seek=new PendingMediaSeek(50,0);Assert.True(seek.Holds(new(10,100,0,true),.3));Assert.False(seek.Holds(new(50,100,0,true),.3));Assert.False(seek.Holds(new(10,100,0,true),3));}
 [Fact] public void LyricSelectionRespectsBoundariesAndBackwardSeeks(){var times=new[]{1.0,2.0,5.0};Assert.Equal(-1,MediaPresentation.ActiveLine(times,x=>x,0));Assert.Equal(0,MediaPresentation.ActiveLine(times,x=>x,1));Assert.Equal(2,MediaPresentation.ActiveLine(times,x=>x,5));Assert.Equal(0,MediaPresentation.ActiveLine(times,x=>x,1.5));}
 [Fact] public void CapsuleFormatsMissingArtist(){Assert.Equal("Song – Artist",MediaPresentation.TitleArtist("Song","Artist"));Assert.Equal("Song",MediaPresentation.TitleArtist("Song",""));}
 [Fact]public async Task CommandReportsRejectionAndTimeout(){using var c=new MediaSeekExecutor(TimeSpan.FromMilliseconds(20));Assert.Equal(MediaSeekResult.Rejected,await c.ExecuteAsync(_=>Task.FromResult(false)));Assert.Equal(MediaSeekResult.TimedOut,await c.ExecuteAsync(_=>new TaskCompletionSource<bool>().Task));}
 [Fact]public async Task NewSeekCancelsOldSeek(){using var c=new MediaSeekExecutor();var old=c.ExecuteAsync(t=>Task.Delay(10000,t).ContinueWith(_=>true));var next=c.ExecuteAsync(_=>Task.FromResult(true));Assert.Equal(MediaSeekResult.Cancelled,await old);Assert.Equal(MediaSeekResult.Accepted,await next);}
}