using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace MochiDock.Services;

public enum GowaEngineSource { Missing, Bundled, Downloaded }

/// <summary>What <see cref="GowaInstaller.Inspect"/> found. <see cref="HasDownloadedCopy"/> can be true next to a bundled engine.</summary>
public sealed record GowaEngineInfo(GowaEngineSource Source, long SizeBytes, string? Version, bool HasDownloadedCopy);

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

    /// <summary>Copy shipped next to the app by builds before on-demand download; upgrades keep it, and it wins over a downloaded one.</summary>
    public static string BundledExePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GOWA", ExeName);

    /// <summary>Written next to a downloaded engine; holds the pinned release it came from.</summary>
    public const string VersionFileName = "version.txt";

    public static string? ResolveLocalExecutable()
    {
        string bundled = BundledExePath;
        if (File.Exists(bundled)) return bundled;
        string downloaded = InstalledExePath;
        if (File.Exists(downloaded)) return downloaded;
        return null;
    }

    /// <summary>Which engine <see cref="ResolveLocalExecutable"/> would run, its size, and whether a downloaded copy exists (removable).</summary>
    public static GowaEngineInfo Inspect(string? bundledExe = null, string? downloadedExe = null)
    {
        bundledExe ??= BundledExePath;
        downloadedExe ??= InstalledExePath;
        bool hasDownloaded = File.Exists(downloadedExe);
        if (File.Exists(bundledExe)) return new(GowaEngineSource.Bundled, SizeOf(bundledExe), null, hasDownloaded);
        if (hasDownloaded) return new(GowaEngineSource.Downloaded, SizeOf(downloadedExe), ReadVersion(downloadedExe), true);
        return new(GowaEngineSource.Missing, 0, null, false);
    }

    /// <summary>Status line for the Settings "Local engine" row. <paramref name="inUse"/>: WhatsApp is on in Local mode.</summary>
    public static string Describe(GowaEngineInfo info, bool inUse) => info.Source switch
    {
        GowaEngineSource.Bundled => $"Bundled with this install ({FormatSize(info.SizeBytes)}). Uninstalling MochiDock removes it."
            + (info.HasDownloadedCopy ? " An unused downloaded copy can be removed." : ""),
        GowaEngineSource.Downloaded => $"Downloaded{(info.Version is { } v ? " " + v : "")} ({FormatSize(info.SizeBytes)})."
            + (inUse ? " Turn WhatsApp off or switch to Remote server to remove it." : " Remove it to free the space; your WhatsApp login is kept."),
        _ => "Not downloaded. It downloads (about 13 MB) when WhatsApp runs on this PC, or now with Download.",
    };

    /// <summary>
    /// Deletes the downloaded engine and its version file; never the bundled copy (the installer owns it).
    /// Waits for a running download first. Returns false when there was nothing to delete; throws when the file is locked.
    /// </summary>
    public static async Task<bool> RemoveDownloadedAsync(string? downloadedExe = null)
    {
        downloadedExe ??= InstalledExePath;
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(downloadedExe)) return false;
            File.Delete(downloadedExe);
            TryDelete(Path.Combine(Path.GetDirectoryName(downloadedExe)!, VersionFileName));
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static long SizeOf(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static string? ReadVersion(string exePath)
    {
        try
        {
            string file = Path.Combine(Path.GetDirectoryName(exePath)!, VersionFileName);
            if (!File.Exists(file)) return null;
            string text = File.ReadAllText(file).Trim();
            return text.Length is > 0 and <= 32 ? text : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string FormatSize(long bytes) => bytes > 0 ? $"{bytes / (1024.0 * 1024.0):0} MB" : "size unknown";

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
                // Only the pinned asset is known to be ReleaseVersion; other sources (tests) leave no version file.
                if (string.Equals(expectedSha256, ReleaseSha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.WriteAllText(Path.Combine(destDir, VersionFileName), ReleaseVersion); }
                    catch (Exception ex) { AppLog.Warn("Gowa", ex); }
                }
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
