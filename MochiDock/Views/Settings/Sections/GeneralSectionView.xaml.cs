using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.Views.Settings.Sections;

/// <summary>General options. Fullscreen and capture bind to the view model; the startup task is toggled through <see cref="StartupService"/>.</summary>
public partial class GeneralSectionView : SettingsSectionBase
{
    public GeneralSectionView() => InitializeComponent();

    protected override void LoadFromSettings()
    {
        StartupCheck.IsChecked = ViewModel?.Settings.StartWithWindows ?? false;

        // Labels carry today's date as the example, so rebuild them each time the page is shown.
        var chosenDate = DateDisplay.Parse(ViewModel?.Settings.DateFormat);
        DateFormatCombo.Items.Clear();
        foreach (var style in Enum.GetValues<DateStyle>())
            DateFormatCombo.Items.Add(new ComboBoxItem { Content = DateDisplay.Label(style, DateTime.Today), Tag = style });
        DateFormatCombo.SelectedItem = DateFormatCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is DateStyle s && s == chosenDate);
    }

    private void DateFormat_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        if (DateFormatCombo.SelectedItem is ComboBoxItem { Tag: DateStyle style }) ViewModel?.UpdateSettings(x => x.DateFormat = style.ToString());
    }

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
