using MochiDock.Core;

public class UpdateChipPolicyTests
{
    [Theory]
    [InlineData(false, false, false, false, false, UpdateChipState.Current)]
    [InlineData(true, false, false, false, false, UpdateChipState.Checking)]
    [InlineData(false, true, true, false, false, UpdateChipState.Install)]
    [InlineData(true, true, true, false, false, UpdateChipState.Install)]      // a re-check keeps the waiting update visible
    [InlineData(false, true, false, false, false, UpdateChipState.Available)]
    [InlineData(false, true, false, true, true, UpdateChipState.Downloading)]
    [InlineData(false, true, false, false, true, UpdateChipState.Installing)]
    public void TheStateFollowsTheUpdateFlow(bool checking, bool hasOffer, bool canInstall, bool downloading, bool installing, UpdateChipState expected) =>
        Assert.Equal(expected, UpdateChipPolicy.State(checking, hasOffer, canInstall, downloading, installing));

    [Fact]
    public void TheTextIsShort()
    {
        Assert.Equal("v0.1.2-beta", UpdateChipPolicy.Text(UpdateChipState.Current, "0.1.2-beta", null, null));
        Assert.Equal("Checking…", UpdateChipPolicy.Text(UpdateChipState.Checking, "0.1.2-beta", null, null));
        Assert.Equal("↑ Update to 0.1.3-beta", UpdateChipPolicy.Text(UpdateChipState.Install, "0.1.2-beta", "0.1.3-beta", null));
        Assert.Equal("↑ 0.1.3-beta available", UpdateChipPolicy.Text(UpdateChipState.Available, "0.1.2-beta", "0.1.3-beta", null));
        Assert.Equal("Downloading 45%", UpdateChipPolicy.Text(UpdateChipState.Downloading, "0.1.2-beta", "0.1.3-beta", 45));
        Assert.Equal("Downloading 100%", UpdateChipPolicy.Text(UpdateChipState.Downloading, "0.1.2-beta", "0.1.3-beta", 130));
        Assert.Equal("Downloading…", UpdateChipPolicy.Text(UpdateChipState.Downloading, "0.1.2-beta", "0.1.3-beta", null));
        Assert.Equal("Installing…", UpdateChipPolicy.Text(UpdateChipState.Installing, "0.1.2-beta", "0.1.3-beta", null));
    }

    [Fact]
    public void OnlyAWaitingUpdateIsHighlighted()
    {
        Assert.True(UpdateChipPolicy.IsHighlighted(UpdateChipState.Install));
        Assert.True(UpdateChipPolicy.IsHighlighted(UpdateChipState.Available));
        Assert.False(UpdateChipPolicy.IsHighlighted(UpdateChipState.Current));
        Assert.False(UpdateChipPolicy.IsHighlighted(UpdateChipState.Checking));
        Assert.False(UpdateChipPolicy.IsHighlighted(UpdateChipState.Downloading));
        Assert.False(UpdateChipPolicy.IsHighlighted(UpdateChipState.Installing));
    }

    [Theory]
    [InlineData(UpdateChipState.Current, true)]
    [InlineData(UpdateChipState.Install, true)]
    [InlineData(UpdateChipState.Available, true)]
    [InlineData(UpdateChipState.Checking, false)]
    [InlineData(UpdateChipState.Downloading, false)]
    [InlineData(UpdateChipState.Installing, false)]
    public void BusyAndDownloadStatesDisableTheHeaderChip(UpdateChipState state, bool expected) =>
        Assert.Equal(expected, UpdateChipPolicy.IsInteractive(state));
}
