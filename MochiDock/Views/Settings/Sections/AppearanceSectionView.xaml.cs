using System;
using System.Windows;
using System.Windows.Controls;
using MochiDock.Controls;
using MochiDock.Core;
using MochiDock.Services;
using MochiDock.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Settings.Sections;

/// <summary>Theme, dock style, monitor and capsule content. Writes go through <see cref="MainViewModel.UpdateSettings"/>; fullscreen and capture options bind to the view model.</summary>
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
        {
            string theme = MochiDock.Core.ThemePolicy.Normalize(settings.CapsuleTheme);
            ThemeMatchaRadio.IsChecked = theme == MochiDock.Core.ThemePolicy.Matcha;
            ThemeHojichaRadio.IsChecked = theme == MochiDock.Core.ThemePolicy.Hojicha;
            ThemeDefaultRadio.IsChecked = theme == MochiDock.Core.ThemePolicy.Default;
            bool notch = string.Equals(settings.DockStyle, "Notch", StringComparison.OrdinalIgnoreCase);
            DockNotchRadio.IsChecked = notch;
            DockFloatingRadio.IsChecked = !notch;
            int chosen = vm.InterfaceScalePercent;
            ScaleCombo.SelectedItem = ScaleCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is int p && p == chosen);
            ScaleRow.Validation = vm.IsInterfaceScaleLimited ? $"Limited to {vm.EffectiveInterfaceScalePercent}% on this display." : "";
            if (!TopMarginBox.IsKeyboardFocused) TopMarginBox.Text = settings.TopMargin.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            if (!ScrollSpeedSlider.IsMouseCaptureWithin) ScrollSpeedSlider.Value = settings.CapsuleScrollSpeed;
            WeatherCheck.IsChecked = settings.ShowWeatherInCapsule;
            bool even = MochiDock.Core.CapsuleClockLayout.Parse(settings.CapsuleClockLayout) == MochiDock.Core.CapsuleClockAlignment.EvenSpacing;
            ClockEvenRadio.IsChecked = even;
            ClockEdgesRadio.IsChecked = !even;
            ExpressiveCheck.IsChecked = settings.ExpressiveMochi;
            LoadBirthday(settings.BirthdayMonthDay);
            // Labels carry today's date as the example, so rebuild them each time the page is shown.
            var chosenDate = DateDisplay.Parse(settings.DateFormat);
            DateFormatCombo.Items.Clear();
            foreach (var style in Enum.GetValues<DateStyle>())
                DateFormatCombo.Items.Add(new ComboBoxItem { Content = DateDisplay.Label(style, DateTime.Today), Tag = style });
            DateFormatCombo.SelectedItem = DateFormatCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is DateStyle s && s == chosenDate);
        }
    }

    private void DateFormat_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        if (DateFormatCombo.SelectedItem is ComboBoxItem { Tag: DateStyle style }) ViewModel?.UpdateSettings(x => x.DateFormat = style.ToString());
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
        string theme = ThemeMatchaRadio.IsChecked == true ? MochiDock.Core.ThemePolicy.Matcha
                     : ThemeHojichaRadio.IsChecked == true ? MochiDock.Core.ThemePolicy.Hojicha
                     : MochiDock.Core.ThemePolicy.Default;
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

    // Month and day pickers; "None" in either clears the birthday. The month list is built once.
    private void LoadBirthday(string? monthDay)
    {
        if (BirthdayMonthCombo.Items.Count == 0)
        {
            BirthdayMonthCombo.Items.Add(new ComboBoxItem { Content = "None", Tag = 0 });
            for (int m = 1; m <= 12; m++)
                BirthdayMonthCombo.Items.Add(new ComboBoxItem { Content = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m), Tag = m });
            BirthdayDayCombo.Items.Add(new ComboBoxItem { Content = "-", Tag = 0 });
            for (int d = 1; d <= 31; d++) BirthdayDayCombo.Items.Add(new ComboBoxItem { Content = d.ToString(System.Globalization.CultureInfo.InvariantCulture), Tag = d });
        }
        var parsed = MochiDock.Core.MascotTriggers.ParseMonthDay(monthDay);
        BirthdayMonthCombo.SelectedIndex = parsed?.Month ?? 0;
        BirthdayDayCombo.SelectedIndex = parsed?.Day ?? 0;
    }

    private void Birthday_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        int month = (BirthdayMonthCombo.SelectedItem as ComboBoxItem)?.Tag is int m ? m : 0;
        int day = (BirthdayDayCombo.SelectedItem as ComboBoxItem)?.Tag is int d ? d : 0;
        if (month == 0 || day == 0)
        {
            BirthdayRow.Validation = month == 0 && day == 0 ? "" : "Choose both a month and a day.";
            if (month == 0 && day == 0) ViewModel?.UpdateSettings(x => x.BirthdayMonthDay = "");
            return;
        }
        string value = $"{month:00}-{day:00}";
        if (MochiDock.Core.MascotTriggers.ParseMonthDay(value) == null) { BirthdayRow.Validation = "That day does not exist in this month."; return; }
        BirthdayRow.Validation = "";
        ViewModel?.UpdateSettings(x => x.BirthdayMonthDay = value);
        ViewModel?.RefreshDayFace();
    }

    private void Expressive_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        bool on = ExpressiveCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => x.ExpressiveMochi = on);
    }

    private void ClockLayout_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        string value = ClockEvenRadio.IsChecked == true ? nameof(MochiDock.Core.CapsuleClockAlignment.EvenSpacing) : nameof(MochiDock.Core.CapsuleClockAlignment.EqualEdges);
        ViewModel?.UpdateSettings(x => x.CapsuleClockLayout = value, SettingsApply.CapsuleRefresh);
    }

    private void Weather_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        bool show = WeatherCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => x.ShowWeatherInCapsule = show, SettingsApply.CapsuleRefresh);
    }
}
