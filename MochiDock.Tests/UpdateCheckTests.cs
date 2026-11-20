using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MochiDock.Core;
using MochiDock.Models;
using MochiDock.Services;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v0.1.0-beta", 0, 1, 0, "beta")]
    [InlineData("1.2.3", 1, 2, 3, "")]
    [InlineData("1.2", 1, 2, 0, "")]
    [InlineData("0.1.0-beta+abc123", 0, 1, 0, "beta")]
    public void ParsesTags(string text, int major, int minor, int patch, string pre)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var v));
        Assert.Equal(new ReleaseVersion(major, minor, patch, pre), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.x.0")]
    [InlineData("1.2.3.4")]
    [InlineData("1.0.0-")]
    public void RejectsNonVersions(string text) => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Theory]
    [InlineData("0.2.0", "0.1.9", true)]
    [InlineData("1.0.0", "1.0.0-beta", true)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1", true)]
    [InlineData("1.0.0-beta.10", "1.0.0-beta.9", true)]
    [InlineData("1.0.0-rc", "1.0.0-beta", true)]
    [InlineData("0.1.0-beta", "0.1.0-beta", false)]
    [InlineData("0.1.0-beta", "0.1.1-beta", false)]
    public void OrdersVersions(string newer, string older, bool isNewer)
    {
        ReleaseVersion.TryParse(newer, out var a);
        ReleaseVersion.TryParse(older, out var b);
        Assert.Equal(isNewer, a > b);
    }
}

public class UpdateFeedTests
{
    const string Repo = "https://github.com/sleepisfun17/MochiDock/releases/tag/";
    static string Release(string tag, bool pre = false, bool draft = false, string? url = null) =>
        $"{{\"tag_name\":\"{tag}\",\"prerelease\":{pre.ToString().ToLower()},\"draft\":{draft.ToString().ToLower()},\"html_url\":\"{url ?? Repo + tag}\"}}";
    static string Feed(params string[] items) => "[" + string.Join(",", items) + "]";

    [Fact]
    public void OffersTheNewestReleaseToABetaBuild()
    {
        var offer = UpdateFeed.Newest(Feed(Release("v0.1.0-beta", true), Release("v0.2.0-beta", true), Release("v0.1.5-beta", true)), "0.1.0-beta");
        Assert.Equal("0.2.0-beta", offer!.Version);
        Assert.Equal(Repo + "v0.2.0-beta", offer.Url);
    }

    [Fact]
    public void NothingWhenCurrent() =>
        Assert.Null(UpdateFeed.Newest(Feed(Release("v0.1.0-beta", true)), "0.1.0-beta"));

    [Fact]
    public void DraftsAreIgnored() =>
        Assert.Null(UpdateFeed.Newest(Feed(Release("v9.0.0", draft: true)), "0.1.0-beta"));

    [Fact]
    public void AStableBuildIsNotOfferedPreReleases()
    {
        Assert.Null(UpdateFeed.Newest(Feed(Release("v2.0.0-beta", true)), "1.0.0"));
        Assert.Equal("1.1.0", UpdateFeed.Newest(Feed(Release("v2.0.0-beta", true), Release("v1.1.0")), "1.0.0")!.Version);
    }

    [Fact]
    public void AReleaseOfTheSameVersionBeatsItsBeta() =>
        Assert.Equal("0.1.0", UpdateFeed.Newest(Feed(Release("v0.1.0")), "0.1.0-beta")!.Version);

    [Fact]
    public void ReleasesOutsideThisProjectAreNeverOffered() =>
        Assert.Null(UpdateFeed.Newest(Feed(Release("v5.0.0", url: "https://evil.example/MochiDock/releases/v5")), "0.1.0-beta"));

    [Fact]
    public void BadFeedsThrowAndUnknownCurrentVersionOffersNothing()
    {
        Assert.ThrowsAny<JsonException>(() => UpdateFeed.Newest("{\"message\":\"Not Found\"}", "0.1.0-beta"));
        Assert.Null(UpdateFeed.Newest(Feed(Release("v5.0.0")), "not-a-version"));
    }
}

public class UpdateCheckServiceTests
{
    sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "[]";
        public Exception? Throw;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Throw != null) throw Throw;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }

    static UpdateCheckService Service(Handler h) => new(new HttpClient(h));

    [Fact]
    public async Task ReportsAnAvailableUpdate()
    {
        var h = new Handler { Body = "[{\"tag_name\":\"v0.2.0-beta\",\"prerelease\":true,\"draft\":false,\"html_url\":\"https://github.com/sleepisfun17/MochiDock/releases/tag/v0.2.0-beta\"}]" };
        var r = await Service(h).CheckAsync("0.1.0-beta", CancellationToken.None);
        Assert.Equal(UpdateCheckState.Available, r.State);
        Assert.Equal("0.2.0-beta", r.Offer!.Version);
    }

    [Fact]
    public async Task UpToDateWhenNothingNewer()
    {
        var r = await Service(new Handler()).CheckAsync("0.1.0-beta", CancellationToken.None);
        Assert.Equal(UpdateCheckState.UpToDate, r.State);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HttpErrorsAreAQuietFailure(HttpStatusCode status)
    {
        var r = await Service(new Handler { Status = status }).CheckAsync("0.1.0-beta", CancellationToken.None);
        Assert.Equal(UpdateCheckState.Failed, r.State);
        Assert.Null(r.Offer);
    }

    [Fact]
    public async Task OfflineAndGarbageBodiesFailWithoutThrowing()
    {
        Assert.Equal(UpdateCheckState.Failed, (await Service(new Handler { Throw = new HttpRequestException("offline") }).CheckAsync("0.1.0-beta", CancellationToken.None)).State);
        Assert.Equal(UpdateCheckState.Failed, (await Service(new Handler { Body = "<html>" }).CheckAsync("0.1.0-beta", CancellationToken.None)).State);
    }

    [Fact]
    public async Task CancellationIsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(new Handler { Throw = new OperationCanceledException(cts.Token) }).CheckAsync("0.1.0-beta", cts.Token));
    }
}

public class VersionSourceTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MochiDock", "MochiDock.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void ProjectAndInstallerShareTheSameVersion()
    {
        string root = Root();
        string project = File.ReadAllText(Path.Combine(root, "MochiDock", "MochiDock.csproj"));
        string installer = File.ReadAllText(Path.Combine(root, "installer", "MochiDock.iss"));
        string fromProject = Regex.Match(project, "<Version>([^<]+)</Version>").Groups[1].Value;
        string fromInstaller = Regex.Match(installer, "#define MyAppVersion \"([^\"]+)\"").Groups[1].Value;
        Assert.True(ReleaseVersion.TryParse(fromProject, out _));
        Assert.Equal(fromProject, fromInstaller);
    }

    [Fact]
    public void UpdateChecksAreOnByDefaultAndOldSettingsStillLoad()
    {
        Assert.True(new AppSettings().CheckForUpdates);
        Assert.True(JsonSerializer.Deserialize<AppSettings>("{}")!.CheckForUpdates);
    }
}
