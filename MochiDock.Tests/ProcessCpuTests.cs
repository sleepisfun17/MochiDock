using MochiDock.Core;
public class ProcessCpuTests {
 [Fact] public void RequiresBaselineAndNormalizesAcrossCores() { var s=new ProcessCpuSampler(); Assert.Null(s.Sample(1,1,0,10,4)); Assert.Equal(25,s.Sample(1,1,10000000,11,4)); Assert.Equal(0,s.Sample(1,1,10000000,12,4)); }
 [Fact] public void ReusedPidAndExitedProcessNeedNewBaselines() { var s=new ProcessCpuSampler(); s.Sample(1,1,0,1,4); Assert.Null(s.Sample(1,2,0,2,4)); s.Retain(Array.Empty<(int,long)>()); Assert.Null(s.Sample(1,2,0,3,4)); }
 [Fact] public void RejectsInvalidIntervalsAndCounterResets() { var s=new ProcessCpuSampler(); s.Sample(1,1,100,1,4); Assert.Null(s.Sample(1,1,50,2,4)); Assert.Null(s.Sample(1,1,60,2,4)); }
 [Fact] public void TrackIdentityUsesAlbumAndRoundedDuration() { Assert.Equal(LyricTrackIdentity.Create("s","a","b","c",10.1),LyricTrackIdentity.Create("s","a","b","c",10.2)); Assert.NotEqual(LyricTrackIdentity.Create("s","a","b","c",10),LyricTrackIdentity.Create("s","a","b","d",10)); }
}