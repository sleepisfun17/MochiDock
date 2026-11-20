using MochiDock.Core;

public class UpdateInstallerAssetTests
{
    const string Repo = "https://github.com/sleepisfun17/MochiDock/";
    static readonly string Hash = new string('a', 64);

    static string Asset(string version, string? name = null, string? url = null, string? digest = null, long size = 40_000_000) =>
        $"{{\"name\":\"{name ?? $"MochiDock-Setup-{version}.exe"}\",\"browser_download_url\":\"{url ?? $"{Repo}releases/download/v{version}/MochiDock-Setup-{version}.exe"}\",\"digest\":{(digest == "none" ? "null" : $"\"{digest ?? "sha256:" + Hash}\"")},\"size\":{size}}}";

    static string Feed(string version, params string[] assets) =>
        $"[{{\"tag_name\":\"v{version}\",\"prerelease\":true,\"draft\":false,\"html_url\":\"{Repo}releases/tag/v{version}\",\"assets\":[{string.Join(",", assets)}]}}]";

    [Fact]
    public void AVerifiableSetupIsOffered()
    {
        var offer = UpdateFeed.Newest(Feed("0.2.0-beta", Asset("0.2.0-beta", name: "notes.txt", url: Repo + "releases/download/v0.2.0-beta/notes.txt"), Asset("0.2.0-beta")), "0.1.0-beta")!;
        Assert.NotNull(offer.Installer);
        Assert.Equal("MochiDock-Setup-0.2.0-beta.exe", offer.Installer!.Name);
        Assert.Equal(Hash, offer.Installer.Sha256);
        Assert.Equal(40_000_000, offer.Installer.Size);
        Assert.StartsWith(UpdateFeed.DownloadPrefix, offer.Installer.DownloadUrl);
    }

    [Fact]
    public void ADigestIsReadCaseInsensitively() =>
        Assert.Equal(Hash, UpdateFeed.Newest(Feed("0.2.0-beta", Asset("0.2.0-beta", digest: "sha256:" + Hash.ToUpperInvariant())), "0.1.0-beta")!.Installer!.Sha256);

    [Theory]
    [InlineData("name")]          // a setup for another version
    [InlineData("otherRepo")]     // hosted by someone else
    [InlineData("http")]          // not https
    [InlineData("noDigest")]      // GitHub reports no checksum
    [InlineData("badDigest")]
    [InlineData("md5")]
    [InlineData("tiny")]
    [InlineData("huge")]
    public void AnythingUnverifiableFallsBackToTheReleasePage(string problem)
    {
        string v = "0.2.0-beta";
        string asset = problem switch
        {
            "name" => Asset(v, name: "MochiDock-Setup-0.1.9.exe"),
            "otherRepo" => Asset(v, url: $"https://github.com/someone/MochiDock/releases/download/v{v}/MochiDock-Setup-{v}.exe"),
            "http" => Asset(v, url: $"http://github.com/sleepisfun17/MochiDock/releases/download/v{v}/MochiDock-Setup-{v}.exe"),
            "noDigest" => Asset(v, digest: "none"),
            "badDigest" => Asset(v, digest: "sha256:1234"),
            "md5" => Asset(v, digest: "md5:" + Hash),
            "tiny" => Asset(v, size: 1000),
            _ => Asset(v, size: 900_000_000),
        };
        var offer = UpdateFeed.Newest(Feed(v, asset), "0.1.0-beta");
        Assert.NotNull(offer);              // the update is still announced
        Assert.Null(offer!.Installer);      // but not installed from inside the app
    }

    [Fact]
    public void AReleaseWithoutAssetsStillOffersThePage() =>
        Assert.Null(UpdateFeed.Newest($"[{{\"tag_name\":\"v0.2.0-beta\",\"prerelease\":true,\"html_url\":\"{Repo}releases/tag/v0.2.0-beta\"}}]", "0.1.0-beta")!.Installer);

    [Theory]
    [InlineData(@"C:\Program Files\MochiDock", @"C:\Program Files\MochiDock\", true)]
    [InlineData(@"C:\Program Files\MochiDock\", @"c:\program files\mochidock", true)]
    [InlineData(@"""C:\Program Files\MochiDock\""", @"C:\Program Files\MochiDock\", true)]
    [InlineData(@"C:\Program Files\MochiDock", @"D:\Project\MochiDock\Publish\", false)]
    [InlineData(null, @"C:\Program Files\MochiDock\", false)]
    [InlineData("", "", false)]
    public void OnlyTheInstalledCopyMatches(string? install, string? baseDir, bool expected) =>
        Assert.Equal(expected, InstallLocation.Matches(install, baseDir));
}
