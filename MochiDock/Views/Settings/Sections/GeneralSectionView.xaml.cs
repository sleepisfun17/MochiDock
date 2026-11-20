using System;
using System.Windows;
using MochiDock.Services;

namespace MochiDock.Views.Settings.Sections;

/// <summary>General options. Fullscreen and capture bind to the view model; the startup task is toggled through <see cref="StartupService"/>.</summary>
public partial class GeneralSectionView : SettingsSectionBase
{
    public GeneralSectionView() => InitializeComponent();

    protected override void LoadFromSettings() =>
        StartupCheck.IsChecked = ViewModel?.Settings.StartWithWindows ?? false;

    private async void Startup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        StartupRow.Validation = "";
        StartupCheck.IsEnabled = false;
        try
        {
            bool enabled = await StartupService.ToggleAsync();
            ViewModel.UpdateSettings(x => x.StartWithWindows = enabled);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Settings", ex);
            StartupRow.Validation = "Could not change startup: " + ex.Message;
        }
        finally
        {
            StartupCheck.IsEnabled = true;
            StartupCheck.IsChecked = ViewModel.Settings.StartWithWindows;
        }
    }
}
