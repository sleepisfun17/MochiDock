using System.Text.Json;
using MochiDock.Core;
using MochiDock.Models;

public class UpdateAnnouncePolicyTests
{
    [Fact]
    public void ANewVersionIsAnnouncedOnce()
    {
        Assert.True(UpdateAnnouncePolicy.ShouldAnnounce("0.1.3-beta", "", manual: false));
        Assert.True(UpdateAnnouncePolicy.ShouldAnnounce("0.1.3-beta", null, manual: false));
        Assert.False(UpdateAnnouncePolicy.ShouldAnnounce("0.1.3-beta", "0.1.3-beta", manual: false));
        Assert.False(UpdateAnnouncePolicy.ShouldAnnounce("0.1.3-BETA", " 0.1.3-beta ", manual: false));
        Assert.True(UpdateAnnouncePolicy.ShouldAnnounce("0.1.4-beta", "0.1.3-beta", manual: false));
    }

    [Fact]
    public void AManualCheckOrNoOfferIsNeverAnnounced()
    {
        Assert.False(UpdateAnnouncePolicy.ShouldAnnounce("0.1.3-beta", "", manual: true));
        Assert.False(UpdateAnnouncePolicy.ShouldAnnounce(null, "", manual: false));
        Assert.False(UpdateAnnouncePolicy.ShouldAnnounce("  ", "", manual: false));
    }

    [Fact]
    public void TheAnnouncedVersionIsSavedAndOldSettingsStillLoad()
    {
        var settings = new AppSettings { LastAnnouncedUpdate = "0.1.3-beta" };
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("0.1.3-beta", back.LastAnnouncedUpdate);
        var old = JsonSerializer.Deserialize<AppSettings>("{\"CheckForUpdates\":true}")!;
        Assert.Equal("", old.LastAnnouncedUpdate);
    }

    [Fact]
    public void TheAnnouncedVersionStaysOnThisPc() =>
        Assert.Contains("LastAnnouncedUpdate", SettingsTransfer.LocalOnlyKeys);
}
