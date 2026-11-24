using System.Windows;

namespace MochiDock.Views.Settings.Sections;

/// <summary>
/// Advanced Settings > Taskbar: which readout blocks sit beside the tray, and the device-battery options that used to be in Hardware.
/// The checkboxes bind to the view model; the prayer, weather and battery boxes are greyed with a note while their own feature is off.
/// </summary>
public partial class TaskbarSectionView : SettingsSectionBase
{
    public TaskbarSectionView() => InitializeComponent();

    protected override void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        var s = vm.Settings;
        PrayerCheck.IsEnabled = s.EnablePrayerTimes;
        PrayerNote.Visibility = s.EnablePrayerTimes ? Visibility.Collapsed : Visibility.Visible;
        WeatherCheck.IsEnabled = s.ShowWeatherTab;
        WeatherNote.Visibility = s.ShowWeatherTab ? Visibility.Collapsed : Visibility.Visible;
        BatteryNote.Visibility = s.EnableDeviceBatteries ? Visibility.Collapsed : Visibility.Visible;
    }
}
