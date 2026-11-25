using System.Text.Json;
using MochiDock.Core;
using MochiDock.Models;

public class UpdateNudgePolicyTests
{
    [Fact]
    public void ANewVersionIsNudged()
    {
        Assert.True(UpdateNudgePolicy.ShouldNudge("0.5.0-beta", "", installing: false));
        Assert.True(UpdateNudgePolicy.ShouldNudge("0.5.0-beta", null, installing: false));
    }

    [Fact]
    public void ADismissedVersionIsHiddenIgnoringCaseAndWhitespace()
    {
        Assert.False(UpdateNudgePolicy.ShouldNudge("0.5.0-beta", "0.5.0-beta", installing: false));
        Assert.False(UpdateNudgePolicy.ShouldNudge(" 0.5.0-BETA ", "0.5.0-beta", installing: false));
        Assert.False(UpdateNudgePolicy.ShouldNudge("0.5.0-beta", "  0.5.0-Beta", installing: false));
    }

    [Fact]
    public void ANewerVersionIsShownAgain() =>
        Assert.True(UpdateNudgePolicy.ShouldNudge("0.5.1-beta", "0.5.0-beta", installing: false));

    [Fact]
    public void NothingIsNudgedWithoutAnOfferOrWhileInstalling()
    {
        Assert.False(UpdateNudgePolicy.ShouldNudge(null, "", installing: false));
        Assert.False(UpdateNudgePolicy.ShouldNudge("  ", "", installing: false));
        Assert.False(UpdateNudgePolicy.ShouldNudge("0.5.0-beta", "", installing: true));
    }

    [Fact]
    public void TheDismissedVersionIsSavedOldSettingsStillLoadAndItStaysOnThisPc()
    {
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { DismissedUpdateVersion = "0.5.0-beta" }))!;
        Assert.Equal("0.5.0-beta", back.DismissedUpdateVersion);
        var old = JsonSerializer.Deserialize<AppSettings>("{\"CheckForUpdates\":true}")!;
        Assert.Equal("", old.DismissedUpdateVersion);
        Assert.Contains("DismissedUpdateVersion", SettingsTransfer.LocalOnlyKeys);
    }
}
