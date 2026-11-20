using MochiDock.Core;

public class DisplayAwakeTests
{
    [Theory]
    [InlineData(true, false, true)]    // at the PC, screen on
    [InlineData(false, false, false)]  // at the PC, screen off: pause
    [InlineData(false, true, true)]    // Remote Desktop: the physical monitors are off, the remote screen is in use
    [InlineData(true, true, true)]
    public void ARemoteSessionIgnoresThePhysicalMonitors(bool consoleOn, bool remote, bool expected) =>
        Assert.Equal(expected, LocalFeaturePolicy.DisplayAwake(consoleOn, remote));
}
