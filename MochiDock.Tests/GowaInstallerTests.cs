using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using MochiDock.Services;
using Xunit;

namespace MochiDock.Tests;

public class GowaInstallerTests
{
    private sealed class ByteHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(payload)
            });
    }

    [Theory]
    [InlineData(true, false, false, true)]   // Local WhatsApp on, engine missing (clean reinstall, imported settings): download.
    [InlineData(true, false, true, false)]   // Engine already present (bundled from an upgrade, or downloaded): no download.
    [InlineData(true, true, false, false)]   // Remote mode never needs the local engine.
    [InlineData(false, false, false, false)] // WhatsApp off: nothing to download.
    public void NeedsDownloadOnlyForEnabledLocalModeWithoutEngine(bool enabled, bool remote, bool present, bool expected) =>
        Assert.Equal(expected, GowaInstaller.NeedsDownload(enabled, remote, present));

    // Two separate folders standing in for <app dir>\GOWA and %LocalAppData%\MochiDock\GOWA\bin.
    private static (string Root, string Bundled, string Downloaded) EngineFolders()
    {
        string root = Path.Combine(Path.GetTempPath(), "mochidock-gowaengine-" + Guid.NewGuid().ToString("N"));
        string bundledDir = Path.Combine(root, "app", "GOWA"), downloadedDir = Path.Combine(root, "local", "bin");
        Directory.CreateDirectory(bundledDir);
        Directory.CreateDirectory(downloadedDir);
        return (root, Path.Combine(bundledDir, GowaInstaller.ExeName), Path.Combine(downloadedDir, GowaInstaller.ExeName));
    }

    [Fact]
    public void Inspect_ReportsMissingDownloadedAndBundledLikeTheLookupOrder()
    {
        var (root, bundled, downloaded) = EngineFolders();
        try
        {
            var none = GowaInstaller.Inspect(bundled, downloaded);
            Assert.Equal(GowaEngineSource.Missing, none.Source);
            Assert.False(none.HasDownloadedCopy);

            File.WriteAllBytes(downloaded, new byte[2 * 1024 * 1024]);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(downloaded)!, GowaInstaller.VersionFileName), "v9.10.0");
            var dl = GowaInstaller.Inspect(bundled, downloaded);
            Assert.Equal(GowaEngineSource.Downloaded, dl.Source);
            Assert.Equal("v9.10.0", dl.Version);
            Assert.Equal(2 * 1024 * 1024, dl.SizeBytes);

            // An upgraded install still has the bundled copy; it wins, and the downloaded one stays removable.
            File.WriteAllBytes(bundled, new byte[16]);
            var both = GowaInstaller.Inspect(bundled, downloaded);
            Assert.Equal(GowaEngineSource.Bundled, both.Source);
            Assert.True(both.HasDownloadedCopy);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Describe_TellsWhereTheEngineIsAndWhenItCanBeRemoved()
    {
        var downloaded = new GowaEngineInfo(GowaEngineSource.Downloaded, 33L * 1024 * 1024, "v9.10.0", true);
        Assert.Equal("Downloaded v9.10.0 (33 MB). Remove it to free the space; your WhatsApp login is kept.", GowaInstaller.Describe(downloaded, inUse: false));
        Assert.Contains("Turn WhatsApp off or switch to Remote server", GowaInstaller.Describe(downloaded, inUse: true));
        Assert.Contains("Uninstalling MochiDock removes it", GowaInstaller.Describe(new GowaEngineInfo(GowaEngineSource.Bundled, 33L * 1024 * 1024, null, false), false));
        Assert.StartsWith("Not downloaded.", GowaInstaller.Describe(new GowaEngineInfo(GowaEngineSource.Missing, 0, null, false), false));
    }

    [Fact]
    public async Task RemoveDownloadedAsync_DeletesOnlyTheDownloadedEngineAndItsVersionFile()
    {
        var (root, bundled, downloaded) = EngineFolders();
        try
        {
            File.WriteAllBytes(bundled, new byte[16]);
            File.WriteAllBytes(downloaded, new byte[16]);
            string version = Path.Combine(Path.GetDirectoryName(downloaded)!, GowaInstaller.VersionFileName);
            File.WriteAllText(version, "v9.10.0");

            Assert.True(await GowaInstaller.RemoveDownloadedAsync(downloaded));
            Assert.False(File.Exists(downloaded));
            Assert.False(File.Exists(version));
            Assert.True(File.Exists(bundled));
            Assert.False(await GowaInstaller.RemoveDownloadedAsync(downloaded)); // nothing left to delete
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PinnedReleaseMetadata_IsValidHttpsAndSha256()
    {
        Assert.StartsWith("https://github.com/aldinokemal/go-whatsapp-web-multidevice/releases/download/", GowaInstaller.ReleaseUrl);
        Assert.True(GowaInstaller.ReleaseSize > 0);
        Assert.Equal(64, GowaInstaller.ReleaseSha256.Length);
    }

    [Fact]
    public async Task EnsureInstalledAsync_VerifiesHashAndExtractsExe()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mochidock-gowadl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            byte[] zipBytes = CreateTestZip("dummy-exe-content"u8.ToArray());
            string sha = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
            string targetExe = Path.Combine(dir, GowaInstaller.ExeName);

            using var http = new HttpClient(new ByteHandler(zipBytes));
            string installed = await GowaInstaller.EnsureInstalledAsync(
                httpClient: http,
                downloadUrl: "https://example.invalid/gowa.zip",
                expectedSize: zipBytes.Length,
                expectedSha256: sha,
                targetExePath: targetExe);

            Assert.Equal(targetExe, installed);
            Assert.True(File.Exists(targetExe));
            Assert.Equal("dummy-exe-content"u8.ToArray(), File.ReadAllBytes(targetExe));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task EnsureInstalledAsync_RejectsHashMismatch_AndLeavesNoExe()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mochidock-gowabad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            byte[] zipBytes = CreateTestZip("dummy-exe-content"u8.ToArray());
            string targetExe = Path.Combine(dir, GowaInstaller.ExeName);

            using var http = new HttpClient(new ByteHandler(zipBytes));
            await Assert.ThrowsAsync<InvalidDataException>(() => GowaInstaller.EnsureInstalledAsync(
                httpClient: http,
                downloadUrl: "https://example.invalid/gowa.zip",
                expectedSize: zipBytes.Length,
                expectedSha256: new string('0', 64),
                targetExePath: targetExe));

            Assert.False(File.Exists(targetExe));
            Assert.Empty(Directory.GetFiles(dir, "*.part"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private static byte[] CreateTestZip(byte[] exeBytes)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(GowaInstaller.ExeName);
            using var s = entry.Open();
            s.Write(exeBytes, 0, exeBytes.Length);
        }
        return ms.ToArray();
    }
}
