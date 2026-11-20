using MochiDock.Core;
public class LocalFeaturePolicyTests {
 [Fact] public void SamplingStopsWhenLockedOrDisplayOff(){Assert.False(LocalFeaturePolicy.SampleHardware(true,true,true,true,true,true));Assert.False(LocalFeaturePolicy.SampleHardware(false,false,true,true,true,true));Assert.False(LocalFeaturePolicy.SampleHardware(false,true,false,false,false,false));Assert.True(LocalFeaturePolicy.SampleHardware(false,true,true,false,false,false));}
 [Fact] public void TabNavigationWrapsAndHandlesEmpty(){Assert.Equal(0,LocalFeaturePolicy.NextTab(2,3,true));Assert.Equal(2,LocalFeaturePolicy.NextTab(0,3,false));Assert.Equal(-1,LocalFeaturePolicy.NextTab(0,0,true));}
 [Fact] public void BatteryDoesNotInventUnknownValues(){Assert.Equal("No battery installed",LocalFeaturePolicy.Battery(128,255,1,0));Assert.Equal("Battery —",LocalFeaturePolicy.Battery(255,255,255,0));Assert.Contains("Battery —",LocalFeaturePolicy.Battery(0,255,255,0));Assert.Contains("0%",LocalFeaturePolicy.Battery(0,0,0,1));}
 [Fact] public void FullscreenPolicyRequiresOptIn(){Assert.False(LocalFeaturePolicy.Hide(false,true));Assert.False(LocalFeaturePolicy.Hide(true,false));Assert.True(LocalFeaturePolicy.Hide(true,true));Assert.False(LocalFeaturePolicy.Hide(true,true,keepForGaming:true));}
}
