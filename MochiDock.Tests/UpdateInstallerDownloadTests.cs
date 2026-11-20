using System.Net;
using System.Security.Cryptography;
using MochiDock.Core;
using MochiDock.Services;

public class UpdateInstallerDownloadTests
{
    const string Url = "https://github.com/sleepisfun17/MochiDock/releases/download/v0.2.0-beta/MochiDock-Setup-0.2.0-beta.exe";

    sealed class FakeServer(byte[] body, Func<CancellationToken, Task>? delay = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (delay != null) await delay(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body), RequestMessage = request };
        }
    }

    static byte[] Body(int size) { var b = new byte[size]; new Random(7).NextBytes(b); return b; }
    static UpdateInstallerAsset AssetFor(byte[] body, string? sha = null, long? size = null) =>
        new("MochiDock-Setup-0.2.0-beta.exe", Url, sha ?? Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(), size ?? body.Length);

    static string Target => Path.Combine(UpdateInstaller.Folder, "MochiDock-Setup-0.2.0-beta.exe");

    [Fact]
    public async Task AMatchingDownloadIsKeptAndProgressIsReported()
    {
        var body = Body(300_000);
        var reports = new List<long>();
        var installer = new UpdateInstaller(new HttpClient(new FakeServer(body)));
        string path = await installer.DownloadAsync(AssetFor(body), new SyncProgress(p => reports.Add(p.Done)), CancellationToken.None);
        Assert.Equal(Target, path);
        Assert.Equal(body, await File.ReadAllBytesAsync(path));
        Assert.Equal(body.Length, reports.Last());
        Assert.StartsWith(Path.GetTempPath(), path, StringComparison.OrdinalIgnoreCase);   // the test scratch folder, never the real one
        File.Delete(path);
    }

    [Fact]
    public async Task AChecksumMismatchDeletesTheFile()
    {
        var body = Body(200_000);
        var installer = new UpdateInstaller(new HttpClient(new FakeServer(body)));
        var ex = await Assert.ThrowsAsync<UpdateVerificationException>(() => installer.DownloadAsync(AssetFor(body, sha: new string('0', 64)), null, CancellationToken.None));
        Assert.Contains("checksum", ex.Message);
        Assert.False(File.Exists(Target));
        Assert.False(File.Exists(Target + ".part"));
    }

    [Theory]
    [InlineData(150_000)]   // shorter than the release says
    [InlineData(250_000)]   // longer than the release says
    public async Task AWrongSizeIsRejected(long claimed)
    {
        var body = Body(200_000);
        var installer = new UpdateInstaller(new HttpClient(new FakeServer(body)));
        await Assert.ThrowsAsync<UpdateVerificationException>(() => installer.DownloadAsync(AssetFor(body, size: claimed), null, CancellationToken.None));
        Assert.False(File.Exists(Target + ".part"));
    }

    [Fact]
    public async Task ACancelLeavesNothingBehind()
    {
        var body = Body(100_000);
        using var cts = new CancellationTokenSource();
        var installer = new UpdateInstaller(new HttpClient(new FakeServer(body, async ct => { cts.Cancel(); await Task.Delay(50, ct); })));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.DownloadAsync(AssetFor(body), null, cts.Token));
        Assert.False(File.Exists(Target + ".part"));
    }

    [Fact]
    public async Task OnlyThisProjectsReleasesAreDownloaded()
    {
        var body = Body(10);
        var installer = new UpdateInstaller(new HttpClient(new FakeServer(body)));
        var foreign = AssetFor(body) with { DownloadUrl = "https://example.com/MochiDock-Setup-0.2.0-beta.exe" };
        await Assert.ThrowsAsync<UpdateVerificationException>(() => installer.DownloadAsync(foreign, null, CancellationToken.None));
        var escape = AssetFor(body) with { Name = @"..\evil.exe" };
        await Assert.ThrowsAsync<UpdateVerificationException>(() => installer.DownloadAsync(escape, null, CancellationToken.None));
    }

    [Fact]
    public void TheSetupRunsSilentlyAndStartsMochiDockAgain()
    {
        foreach (var flag in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/RELAUNCH" })
            Assert.Contains(flag, UpdateInstaller.SetupArguments);
    }

    sealed class SyncProgress(Action<(long Done, long Total)> report) : IProgress<(long Done, long Total)>
    {
        public void Report((long Done, long Total) value) => report(value);
    }
}
