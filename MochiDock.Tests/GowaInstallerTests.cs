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
