using System.Text.Json;
using MochiDock.Models;
using Xunit;

public class OutlookTrackedFoldersDefaultTests
{
    [Fact]
    public void NewInstallHasNoPersonalFolders() =>
        Assert.Empty(JsonSerializer.Deserialize<AppSettings>("{}")!.OutlookTrackedFolders);

    [Fact]
    public void ExistingSettingsKeepTheirFolders()
    {
        var s = JsonSerializer.Deserialize<AppSettings>("""{"OutlookTrackedFolders":["案件","Clients","Others"]}""")!;
        Assert.Equal(new[] { "案件", "Clients", "Others" }, s.OutlookTrackedFolders);
    }

    [Fact]
    public void ExplicitlyEmptyListStaysEmpty() =>
        Assert.Empty(JsonSerializer.Deserialize<AppSettings>("""{"OutlookTrackedFolders":[]}""")!.OutlookTrackedFolders);
}
