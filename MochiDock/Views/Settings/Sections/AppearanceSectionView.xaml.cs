using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MochiDock.Core;
using MochiDock.Services;
using MochiDock.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MochiDock.Views.Settings.Sections;

/// <summary>Theme, dock style, monitor and capsule layout. Writes go through <see cref="MainViewModel.UpdateSettings"/>.</summary>
public partial class AppearanceSectionView : SettingsSectionBase
{
    public AppearanceSectionView()
    {
        InitializeComponent();
        foreach (var preset in InterfaceScale.Presets)
            ScaleCombo.Items.Add(new ComboBoxItem { Content = preset == InterfaceScale.Default ? $"{preset}% (default)" : $"{preset}%", Tag = preset });
    }

    protected override void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        var settings = vm.Settings;

        string theme = ThemePolicy.Normalize(settings.CapsuleTheme);
        ThemeMatchaRadio.IsChecked = theme == ThemePolicy.Matcha;
        ThemeHojichaRadio.IsChecked = theme == ThemePolicy.Hojicha;
        ThemeKuroNightRadio.IsChecked = theme == ThemePolicy.KuroNight;
        ThemeDefaultRadio.IsChecked = theme == ThemePolicy.Default;
        bool notch = string.Equals(settings.DockStyle, "Notch", StringComparison.OrdinalIgnoreCase);
        DockNotchRadio.IsChecked = notch;
        DockFloatingRadio.IsChecked = !notch;
        int chosen = vm.InterfaceScalePercent;
        ScaleCombo.SelectedItem = ScaleCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is int p && p == chosen);
        ScaleRow.Validation = vm.IsInterfaceScaleLimited ? $"Limited to {vm.EffectiveInterfaceScalePercent}% on this display." : "";
        if (!TopMarginBox.IsKeyboardFocused) TopMarginBox.Text = settings.TopMargin.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        if (!ScrollSpeedSlider.IsMouseCaptureWithin) ScrollSpeedSlider.Value = settings.CapsuleScrollSpeed;
        WeatherCheck.IsChecked = settings.ShowWeatherInCapsule;
        bool even = CapsuleClockLayout.Parse(settings.CapsuleClockLayout) == CapsuleClockAlignment.EvenSpacing;
        ClockEvenRadio.IsChecked = even;
        ClockEdgesRadio.IsChecked = !even;
    }

    private void Scale_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        if (ScaleCombo.SelectedItem is ComboBoxItem { Tag: int percent } && ViewModel != null) ViewModel.InterfaceScalePercent = percent;
    }

    private void ScaleDown_Click(object sender, RoutedEventArgs e) => ViewModel?.StepInterfaceScale(-1);
    private void ScaleUp_Click(object sender, RoutedEventArgs e) => ViewModel?.StepInterfaceScale(1);

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        string theme = ThemeMatchaRadio.IsChecked == true ? ThemePolicy.Matcha
                     : ThemeHojichaRadio.IsChecked == true ? ThemePolicy.Hojicha
                     : ThemeKuroNightRadio.IsChecked == true ? ThemePolicy.KuroNight
                     : ThemePolicy.Default;
        ViewModel?.UpdateSettings(x => x.CapsuleTheme = theme, SettingsApply.Theme);
    }

    private void DockStyle_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        string style = DockNotchRadio.IsChecked == true ? "Notch" : "Floating";
        ViewModel?.UpdateSettings(x => x.DockStyle = style, SettingsApply.Reposition);
    }

    private void TopMargin_LostFocus(object sender, RoutedEventArgs e) => CommitTopMargin();

    private void TopMargin_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        CommitTopMargin();
        e.Handled = true;
    }

    private void CommitTopMargin()
    {
        if (!IsLoaded || Loading || ViewModel == null) return;
        TopMarginRow.Validation = "";
        if (!SettingsValidation.TryParseTopMargin(TopMarginBox.Text, out double margin))
        {
            TopMarginRow.Validation = $"Enter a number from {SettingsValidation.TopMarginMin:0} to {SettingsValidation.TopMarginMax:0}. Keeping {ViewModel.Settings.TopMargin:0.##}.";
            return;
        }
        if (margin != ViewModel.Settings.TopMargin)
            ViewModel.UpdateSettings(x => x.TopMargin = margin, SettingsApply.Reposition);
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e) =>
        ViewModel?.UpdateSettings(x => x.IslandHorizontalRatio = null, SettingsApply.Reposition);

    private void ScrollSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || Loading) return;
        ViewModel?.SetCapsuleScrollSpeed(ScrollSpeedSlider.Value);
    }

    private void ClockLayout_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        string value = ClockEvenRadio.IsChecked == true ? nameof(CapsuleClockAlignment.EvenSpacing) : nameof(CapsuleClockAlignment.EqualEdges);
        ViewModel?.UpdateSettings(x => x.CapsuleClockLayout = value, SettingsApply.CapsuleRefresh);
    }

    private void Weather_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        bool show = WeatherCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => x.ShowWeatherInCapsule = show, SettingsApply.CapsuleRefresh);
    }
}
