using UserControl = System.Windows.Controls.UserControl;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MochiDock.Services;
using MochiDock.ViewModels;

namespace MochiDock.Views.Settings.Sections;

/// <summary>Data &amp; diagnostics: export/import/reload, files, logs and integration status. DataContext is the <see cref="MainViewModel"/>.</summary>
public partial class DataSectionView : UserControl
{
    public DataSectionView()
    {
        InitializeComponent();
        IsVisibleChanged += (s, e) => { if (IsVisible) RefreshIssueCount(); };
        Loaded += (s, e) => { RefreshIssueCount(); LoadUpdateOption(); };
    }

    private bool _loadingUpdate;

    private void LoadUpdateOption()
    {
        _loadingUpdate = true;
        try { AutoUpdateCheck.IsChecked = ViewModel?.Settings.CheckForUpdates ?? true; }
        finally { _loadingUpdate = false; }
    }

    private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _loadingUpdate) return;
        bool on = AutoUpdateCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => x.CheckForUpdates = on);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is System.Windows.Controls.Button button)
            await MochiDock.Controls.RefreshFeedback.RunAsync(button, () => vm.CheckForUpdatesAsync(manual: true));
    }

    private void ViewRelease_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenUpdatePage();

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.InstallUpdateAsync();
    }

    private void CancelUpdate_Click(object sender, RoutedEventArgs e) => ViewModel?.CancelUpdateDownload();

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void RefreshIssueCount()
    {
        int count = AppLog.WarningCount;
        IssueCountText.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        IssueCountText.Text = count == 1 ? "1 issue logged" : $"{count} issues logged";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "MochiDock settings (*.json)|*.json", FileName = "MochiDock-settings.json" };
        if (dialog.ShowDialog() != true) return;
        try { SettingsService.Export(dialog.FileName); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "MochiDock settings"); }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        ImportRow.Validation = "";
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "MochiDock settings (*.json)|*.json" };
        if (dialog.ShowDialog() != true || ViewModel == null) return;
        try
        {
            ViewModel.ApplyImportedSettings(SettingsService.Import(dialog.FileName));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Settings", ex);
            ImportRow.Validation = "Not imported: " + ex.Message;
        }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        ReloadRow.Validation = "";
        if (ViewModel == null || sender is not System.Windows.Controls.Button button) return;
        try { await MochiDock.Controls.RefreshFeedback.RunAsync(button, async () => await ViewModel.ReloadSettingsAsync()); }
        catch (Exception ex)
        {
            AppLog.Warn("Settings", ex);
            ReloadRow.Validation = "Not reloaded: " + ex.Message;
        }
    }

    private void ShowSettingsFile_Click(object sender, RoutedEventArgs e) => Reveal(SettingsService.SettingsFilePath);

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) =>
        Reveal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MochiDock"));

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.Directory);
            Start("explorer.exe", "\"" + AppLog.Directory + "\"");
        }
        catch (Exception ex) { AppLog.Warn("Settings", ex); }
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is System.Windows.Controls.Button button) await MochiDock.Controls.RefreshFeedback.RunAsync(button, vm.RetryIntegrations);
    }

    private static void Reveal(string path)
    {
        try
        {
            if (File.Exists(path)) Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path)) Start("explorer.exe", "\"" + path + "\"");
        }
        catch (Exception ex) { AppLog.Warn("Settings", ex); }
    }

    private static void Start(string file, string args) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args) { UseShellExecute = true });
}
