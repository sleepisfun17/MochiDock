using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;
using Clipboard = System.Windows.Clipboard;

namespace MochiDock.Views.Cards;

public partial class DownloadsCardView : UserControl
{
    private MainViewModel? ViewModel => DataContext as MainViewModel;
    private bool _isDownloading;
    private bool _isLoadingFormat;
    private string? _lastFilePath;
    private string _lastError = "";

    public DownloadsCardView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Restore the format used last time.
            _isLoadingFormat = true;
            try
            {
                bool audio = ViewModel?.DownloadAudioFormat == true;
                FormatAudioRadio.IsChecked = audio;
                FormatVideoRadio.IsChecked = !audio;
            }
            finally { _isLoadingFormat = false; }
        };
    }

    private void Format_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingFormat || !IsLoaded || ViewModel == null) return;
        ViewModel.DownloadAudioFormat = FormatAudioRadio.IsChecked == true;
    }

    // Shows only the follow-up buttons that make sense for the last result.
    private void ShowResultActions(bool succeeded, bool failed)
    {
        OpenFolderButton.Visibility = succeeded ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        CopyErrorButton.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        ResultActions.Visibility = succeeded || failed ? Visibility.Visible : Visibility.Collapsed;
        // An error reads as an error; every other state is quiet meta text.
        StatusLogText.SetResourceReference(TextBlock.ForegroundProperty, failed ? "IslandNegativeBrush" : "IslandMutedInkBrush");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MochiDock", "PairedDrop");
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            if (!string.IsNullOrEmpty(_lastFilePath) && File.Exists(_lastFilePath))
                psi.ArgumentList.Add("/select," + _lastFilePath);
            else
                psi.ArgumentList.Add(folder);
            Process.Start(psi);
        }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("Downloads", ex); }
    }

    private void Retry_Click(object sender, RoutedEventArgs e) => StartDownload_Click(sender, e);

    private void CopyError_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(string.IsNullOrWhiteSpace(_lastError) ? StatusLogText.Text : _lastError); }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("Downloads", ex); }
    }

    private void PasteUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var txt = Clipboard.GetText()?.Trim();
                if (!string.IsNullOrEmpty(txt) && (txt.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || txt.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    UrlTextBox.Text = txt;
                }
            }
        }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("Downloads", ex); }
    }

    private async void StartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading) return;
        if (!MochiDock.Core.YtDlpCommand.TryParseUrl(UrlTextBox.Text, out var url))
        {
            StatusTitleText.Text = "Please enter a valid http(s) media URL";
            return;
        }

        _isDownloading = true;
        _lastError = "";
        _lastFilePath = null;
        ShowResultActions(false, false);
        DownloadButton.IsEnabled = false;
        DownloadProgressBar.Value = 0;
        StatusPercentText.Text = "0%";
        StatusTitleText.Text = "Preparing download...";
        StatusLogText.Text = "Checking yt-dlp binary...";

        try
        {
            var ytdlpPath = await EnsureYtDlpAsync();
            if (string.IsNullOrEmpty(ytdlpPath) || !File.Exists(ytdlpPath))
            {
                StatusTitleText.Text = "yt-dlp not available";
                StatusLogText.Text = "Could not find or download yt-dlp.exe. Please install yt-dlp or check internet connection.";
                _lastError = StatusLogText.Text;
                ShowResultActions(false, true);
                _isDownloading = false;
                DownloadButton.IsEnabled = true;
                return;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var downloadDir = Path.Combine(localAppData, "MochiDock", "PairedDrop");
            Directory.CreateDirectory(downloadDir);

            bool isAudio = FormatAudioRadio.IsChecked == true;
            var ffmpegPath = MochiDock.Core.ToolLocator.FindFfmpeg(AppDomain.CurrentDomain.BaseDirectory, localAppData, Environment.GetEnvironmentVariable("PATH"), File.Exists);
            if (ffmpegPath == null && MochiDock.Core.YtDlpCommand.RequiresFfmpeg(isAudio))
            {
                StatusTitleText.Text = "ffmpeg required";
                StatusLogText.Text = MochiDock.Core.ToolLocator.FfmpegMissingMessage;
                _lastError = StatusLogText.Text;
                ShowResultActions(false, true);
                return;
            }
            var ytArgs = MochiDock.Core.YtDlpCommand.BuildArguments(url, downloadDir, isAudio, ffmpegPath);

            StatusTitleText.Text = isAudio ? "Downloading audio..." : "Downloading video...";
            StatusLogText.Text = "Connecting to source...";

            var psi = new ProcessStartInfo
            {
                FileName = ytdlpPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var a in ytArgs) psi.ArgumentList.Add(a);

            using var process = new Process { StartInfo = psi };
            string? lastDownloadedFile = null;

            process.OutputDataReceived += (_, ea) =>
            {
                if (string.IsNullOrEmpty(ea.Data)) return;
                Dispatcher.BeginInvoke(() =>
                {
                    StatusLogText.Text = ea.Data;
                    var match = Regex.Match(ea.Data, @"\[download\]\s+([\d\.]+)%");
                    if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                    {
                        DownloadProgressBar.Value = pct;
                        StatusPercentText.Text = $"{pct:F0}%";
                    }

                    var destMatch = Regex.Match(ea.Data, @"\[(?:download|ExtractAudio|Merger)\] Destination:\s+(.+)");
                    if (destMatch.Success)
                    {
                        lastDownloadedFile = destMatch.Groups[1].Value.Trim();
                    }
                });
            };

            process.ErrorDataReceived += (_, ea) =>
            {
                if (!string.IsNullOrEmpty(ea.Data))
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (ea.Data.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                        {
                            StatusLogText.Text = ea.Data;
                            _lastError = ea.Data;
                        }
                    });
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                StatusTitleText.Text = "Download Complete!";
                ViewModel?.ShowSurprisedMoment();
                DownloadProgressBar.Value = 100;
                StatusPercentText.Text = "100%";
                StatusLogText.Text = "File dropped onto File Shelf.";
                _lastFilePath = lastDownloadedFile;

                // Register with FileShelfService if available
                if (ViewModel?.FileShelfService != null)
                {
                    if (!string.IsNullOrEmpty(lastDownloadedFile) && File.Exists(lastDownloadedFile))
                    {
                        ViewModel.FileShelfService.AddFile(lastDownloadedFile);
                    }
                    else
                    {
                        // Check latest file in drop dir
                        var latestFile = new DirectoryInfo(downloadDir).GetFiles()
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .FirstOrDefault();
                        if (latestFile != null && (DateTime.UtcNow - latestFile.LastWriteTimeUtc).TotalMinutes < 2)
                        {
                            ViewModel.FileShelfService.AddFile(latestFile.FullName);
                        }
                    }
                }
            }
            else
            {
                StatusTitleText.Text = "Download failed";
                if (string.IsNullOrWhiteSpace(_lastError)) _lastError = "yt-dlp exited with code " + process.ExitCode + ". Last output: " + StatusLogText.Text;
                StatusLogText.Text = _lastError;
            }
            ShowResultActions(process.ExitCode == 0, process.ExitCode != 0);
        }
        catch (Exception ex)
        {
            MochiDock.Services.AppLog.Warn("Downloads", ex);
            StatusTitleText.Text = "Download error";
            StatusLogText.Text = ex.Message;
            _lastError = ex.Message;
            ShowResultActions(false, true);
        }
        finally
        {
            _isDownloading = false;
            DownloadButton.IsEnabled = true;
        }
    }

    private static async Task<string?> EnsureYtDlpAsync()
    {
        // 1. Check App local folder
        var localFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MochiDock");
        Directory.CreateDirectory(localFolder);
        var targetPath = Path.Combine(localFolder, "yt-dlp.exe");
        if (File.Exists(targetPath)) return targetPath;

        // 2. Check App directory
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var appPath = Path.Combine(appDir, "yt-dlp.exe");
        if (File.Exists(appPath)) return appPath;

        var toolsPath = Path.Combine(appDir, "Tools", "yt-dlp.exe");
        if (File.Exists(toolsPath)) return toolsPath;

        // 3. Try download standalone binary from GitHub releases
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            const string baseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
            var sums = await client.GetStringAsync(baseUrl + "SHA2-256SUMS");
            var expected = MochiDock.Core.YtDlpCommand.FindExpectedHash(sums, "yt-dlp.exe");
            var bytes = await client.GetByteArrayAsync(baseUrl + "yt-dlp.exe");
            if (bytes.Length > 10000 && MochiDock.Core.YtDlpCommand.HashMatches(bytes, expected))
            {
                var tempPath = targetPath + ".download-" + Guid.NewGuid().ToString("N");
                try
                {
                    await File.WriteAllBytesAsync(tempPath, bytes);
                    File.Move(tempPath, targetPath, overwrite: true);
                }
                finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
                return targetPath;
            }
            MochiDock.Services.AppLog.Warn("Downloads", "yt-dlp download rejected: checksum missing or mismatched.");
        }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("Downloads", ex); }

        // 4. Fallback: check PATH (unverified, user-installed)
        MochiDock.Services.AppLog.Warn("Downloads", "Falling back to yt-dlp.exe from PATH.");
        return "yt-dlp.exe";
    }
}

