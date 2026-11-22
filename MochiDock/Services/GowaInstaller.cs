using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace MochiDock.Services;

/// <summary>
/// Downloads the optional GOWA local WhatsApp binary on demand into %LocalAppData%\MochiDock\GOWA\bin\,
/// verified against a pinned HTTPS release URL, byte size, and SHA-256 digest.
/// </summary>
public static class GowaInstaller
{
    public const string ReleaseVersion = "v9.10.0";
    public const string ReleaseUrl = "https://github.com/aldinokemal/go-whatsapp-web-multidevice/releases/download/v9.10.0/whatsapp_9.10.0_windows_amd64.zip";
    public const long ReleaseSize = 13456970L;
    public const string ReleaseSha256 = "aa60a589e0e964518716d03c2c93c0ea6ce70d74d7c3b09c22c6ad324e27c203";
    public const string ExeName = "windows-amd64.exe";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static string BinFolder => Path.Combine(AppDataPaths.Local, "GOWA", "bin");
    public static string InstalledExePath => Path.Combine(BinFolder, ExeName);

    /// <summary>True when Local WhatsApp is on but no engine exists yet (a clean reinstall, imported settings, a new PC).</summary>
    public static bool NeedsDownload(bool whatsAppEnabled, bool remoteMode, bool enginePresent) =>
        whatsAppEnabled && !remoteMode && !enginePresent;

    public static string? ResolveLocalExecutable()
    {
        string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GOWA", ExeName);
        if (File.Exists(bundled)) return bundled;
        string downloaded = InstalledExePath;
        if (File.Exists(downloaded)) return downloaded;
        return null;
    }

    public static async Task<string> EnsureInstalledAsync(
        IProgress<(long Done, long Total)>? progress = null,
        CancellationToken ct = default,
        HttpClient? httpClient = null,
        string downloadUrl = ReleaseUrl,
        long expectedSize = ReleaseSize,
        string expectedSha256 = ReleaseSha256,
        string? targetExePath = null)
    {
        if (targetExePath == null && ResolveLocalExecutable() is { } existing)
            return existing;
        if (targetExePath != null && File.Exists(targetExePath))
            return targetExePath;

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (targetExePath == null && ResolveLocalExecutable() is { } existingLocked)
                return existingLocked;
            if (targetExePath != null && File.Exists(targetExePath))
                return targetExePath;

            if (!downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GOWA download URL must use HTTPS.");

            string destExe = targetExePath ?? InstalledExePath;
            string destDir = Path.GetDirectoryName(destExe)!;
            Directory.CreateDirectory(destDir);
            string zipPart = Path.Combine(destDir, $"gowa-{Guid.NewGuid():N}.zip.part");
            string exePart = Path.Combine(destDir, $"{ExeName}.{Guid.NewGuid():N}.part");

            HttpClient client = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            bool ownsClient = httpClient == null;
            try
            {
                if (ownsClient) client.DefaultRequestHeaders.UserAgent.ParseAdd(AppUserAgent.Value);
                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri is { } finalUri &&
                    !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                    httpClient == null)
                    throw new InvalidDataException("GOWA download redirected away from HTTPS.");

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long done = 0;
                await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var target = new FileStream(zipPart, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        done += read;
                        if (done > expectedSize)
                            throw new InvalidDataException("GOWA archive exceeded the expected size.");
                        hash.AppendData(buffer, 0, read);
                        await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        progress?.Report((done, expectedSize));
                    }
                }

                if (done != expectedSize)
                    throw new InvalidDataException("GOWA download was incomplete.");
                if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("GOWA download failed SHA-256 verification.");

                using (var archive = ZipFile.OpenRead(zipPart))
                {
                    var entry = archive.Entries.FirstOrDefault(e =>
                        string.Equals(Path.GetFileName(e.FullName), ExeName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException($"'{ExeName}' was not found inside the GOWA archive.");
                    entry.ExtractToFile(exePart, overwrite: true);
                }

                File.Move(exePart, destExe, overwrite: true);
                return destExe;
            }
            finally
            {
                if (ownsClient) client.Dispose();
                TryDelete(zipPart);
                TryDelete(exePart);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
