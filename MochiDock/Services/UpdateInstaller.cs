using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using MochiDock.Core;
using Microsoft.Win32;

namespace MochiDock.Services;

/// <summary>The downloaded setup did not match what the release says it should be; it was deleted and not run.</summary>
public sealed class UpdateVerificationException(string message) : Exception(message);

/// <summary>
/// One-click update: downloads the release's setup from this project's GitHub releases, checks its size and SHA-256 against the
/// digest GitHub reports for the asset, and starts it silently. Only the copy the setup installed updates itself; a portable or
/// development copy is sent to the release page instead.
/// </summary>
public sealed class UpdateInstaller
{
    /// <summary>The setup's fixed AppId (installer/MochiDock.iss); its uninstall key records where MochiDock is installed.</summary>
    public const string AppId = "{6B1E3C52-8F0A-4D7B-9A2E-4C5D7E8F1A93}";

    /// <summary>Silent install, no message boxes or restart prompt, and MochiDock started again when it is done (/RELAUNCH, see the setup's [Run]).</summary>
    public const string SetupArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /RELAUNCH";

    public static string Folder => Path.Combine(AppDataPaths.Local, "Updates");

    private readonly HttpClient _http;

    public UpdateInstaller(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        if (http is null) _http.DefaultRequestHeaders.UserAgent.ParseAdd("MochiDock");
    }

    /// <summary>True when this copy runs from the folder the setup installed (its uninstall entry's InstallLocation).</summary>
    public static bool IsInstalledCopy()
    {
        try
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}_is1");
                if (key?.GetValue("InstallLocation") is string location && InstallLocation.Matches(location, AppContext.BaseDirectory)) return true;
            }
        }
        catch (Exception ex) { AppLog.Warn("Update", ex); }
        return false;
    }

    /// <summary>
    /// Downloads the setup to the Updates folder, hashing as it goes, and returns its path once its size and SHA-256 match the release.
    /// A mismatch deletes the file and throws <see cref="UpdateVerificationException"/>; a cancel or failure leaves no partial file.
    /// </summary>
    public async Task<string> DownloadAsync(UpdateInstallerAsset asset, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        if (!asset.DownloadUrl.StartsWith(UpdateFeed.DownloadPrefix, StringComparison.Ordinal) || Path.GetFileName(asset.Name) != asset.Name)
            throw new UpdateVerificationException("The update does not come from MochiDock's releases, so it was not downloaded.");
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, asset.Name), partial = path + ".part";
        try
        {
            using var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } final && final.Scheme != Uri.UriSchemeHttps)
                throw new UpdateVerificationException("The update was not served over a secure connection, so it was not installed.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long done = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    done += read;
                    if (done > asset.Size) throw new UpdateVerificationException("The download was larger than the release says, so it was not installed.");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    progress?.Report((done, asset.Size));
                }
            }
            if (done != asset.Size) throw new UpdateVerificationException("The download was incomplete, so it was not installed. Try again.");
            if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateVerificationException("The download didn't match the release's checksum, so it was not installed.");
            File.Move(partial, path, overwrite: true);
            return path;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    /// <summary>Starts the verified setup silently; it closes MochiDock, installs over it and starts it again.</summary>
    public static void Launch(string setupPath) =>
        Process.Start(new ProcessStartInfo(setupPath, SetupArguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(setupPath) });

    /// <summary>Removes setups left from an earlier update (run at startup; the running copy is already the new one).</summary>
    public static void CleanUp()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var file in Directory.EnumerateFiles(Folder)) TryDelete(file);
        }
        catch (Exception ex) { AppLog.Warn("Update", ex); }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { AppLog.Warn("Update", ex); }
    }
}
