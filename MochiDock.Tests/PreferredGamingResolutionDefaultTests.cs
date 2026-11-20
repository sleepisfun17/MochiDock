using System.Text.Json;
using MochiDock.Models;
using Xunit;

public class PreferredGamingResolutionDefaultTests
{
    [Fact]
    public void NewInstallStoresNoMonitorSpecificResolution() =>
        Assert.Equal("", JsonSerializer.Deserialize<AppSettings>("{}")!.PreferredGamingResolution);

    [Fact]
    public void ExistingSettingsKeepTheirResolution() =>
        Assert.Equal("2304x1440", JsonSerializer.Deserialize<AppSettings>("""{"PreferredGamingResolution":"2304x1440"}""")!.PreferredGamingResolution);
}
