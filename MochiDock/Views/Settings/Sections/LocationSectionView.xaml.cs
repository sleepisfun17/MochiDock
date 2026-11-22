using System;
using System.Windows;
using System.Windows.Controls;
using MochiDock.Services;
using MochiDock.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ComboBox = System.Windows.Controls.ComboBox;

namespace MochiDock.Views.Settings.Sections;

/// <summary>City, weather and prayer settings. Writes go through <see cref="MainViewModel.UpdateSettings"/>; the prayer switches and sound bind to the view model.</summary>
public partial class LocationSectionView : SettingsSectionBase
{
    private int _checkRequest;

    public LocationSectionView() => InitializeComponent();

    protected override void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        var settings = vm.Settings;
        if (!CityBox.IsKeyboardFocused) CityBox.Text = string.IsNullOrWhiteSpace(settings.WeatherCity) ? "Tokyo" : settings.WeatherCity;
        PrayerCapsuleCheck.IsChecked = settings.ShowPrayerInCapsule;
        Select(MethodCombo, string.IsNullOrWhiteSpace(settings.PrayerCalculationMethod) ? "MWL" : settings.PrayerCalculationMethod);
        Select(SoundCombo, vm.PrayerSoundMode);
    }

    private static void Select(ComboBox combo, string tag)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private void City_LostFocus(object sender, RoutedEventArgs e) => CommitCity();

    private void City_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        CommitCity();
        e.Handled = true;
    }

    private void CommitCity()
    {
        if (!IsLoaded || Loading || ViewModel == null) return;
        string city = string.IsNullOrWhiteSpace(CityBox.Text) ? "Tokyo" : CityBox.Text.Trim();
        if (!string.Equals(ViewModel.Settings.WeatherCity, city, StringComparison.OrdinalIgnoreCase))
            ViewModel.UpdateSettings(x => x.WeatherCity = city, SettingsApply.Weather | SettingsApply.PrayerLocation);
    }

    private async void CheckCity_Click(object sender, RoutedEventArgs e)
    {
        string query = CityBox.Text.Trim();
        if (query.Length == 0)
        {
            Show("Type a city, district or ward name first.", "IslandNegativeBrush");
            return;
        }
        if (ViewModel == null) return;
        int request = ++_checkRequest;
        Show($"Searching for '{query}'…", "IslandStatusBrush");
        try
        {
            var result = await ViewModel.ResolveLocationAsync(query);
            if (request != _checkRequest) return;
            if (result.Success)
            {
                Show($"✓ Resolved: {result.DisplayName} ({result.Latitude:0.00}°, {result.Longitude:0.00}°)", "IslandPositiveBrush");
                if (ViewModel != null && !string.Equals(ViewModel.Settings.WeatherCity, query, StringComparison.Ordinal))
                    ViewModel.UpdateSettings(x => x.WeatherCity = query, SettingsApply.Weather | SettingsApply.PrayerLocation);
            }
            else Show($"✕ '{query}' was not found. Try adding the city or country, for example 'Shibuya, Tokyo' or 'Serpong, Indonesia'.", "IslandNegativeBrush");
        }
        catch (Exception ex)
        {
            if (request == _checkRequest) Show($"Could not check the city: {ex.Message}", "IslandNegativeBrush");
        }
    }

    private void Show(string text, string brushKey)
    {
        CityResultText.Text = text;
        CityResultText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
    }

    private void Method_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Loading || ViewModel == null) return;
        if (MethodCombo.SelectedItem is ComboBoxItem { Tag: string tag } && tag != ViewModel.Settings.PrayerCalculationMethod)
            ViewModel.UpdateSettings(x => x.PrayerCalculationMethod = tag, SettingsApply.PrayerLocation);
    }

    private void Sound_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Loading || ViewModel == null) return;
        if (SoundCombo.SelectedItem is ComboBoxItem { Tag: string tag }) ViewModel.PrayerSoundMode = tag;
    }

    private void PrayerCapsule_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        bool show = PrayerCapsuleCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => x.ShowPrayerInCapsule = show, SettingsApply.CapsuleRefresh);
    }
}
