using System.Windows;
using System.Windows.Controls;
using MochiDock.Core;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Settings.Sections;

/// <summary>Hardware alert thresholds. Alerts and the battery module bind to the view model; thresholds write through <see cref="MainViewModel.UpdateSettings"/>.</summary>
public partial class HardwareSectionView : SettingsSectionBase
{
    public HardwareSectionView()
    {
        InitializeComponent();
        Loaded += (s, e) => FillComponentKinds();
    }

    protected override void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        {
            if (!CpuSlider.IsMouseCaptureWithin) CpuSlider.Value = vm.Settings.CpuAlertThreshold;
            if (!GpuSlider.IsMouseCaptureWithin) GpuSlider.Value = vm.Settings.GpuAlertThreshold;
            if (!TempSlider.IsMouseCaptureWithin) TempSlider.Value = vm.Settings.TemperatureAlertThreshold;
            if (!HeatSlider.IsMouseCaptureWithin) HeatSlider.Value = Math.Clamp(vm.Settings.SustainedHeatThreshold, 60, 95);
            int minutes = vm.Settings.SustainedHeatMinutes;
            HeatMinutesCombo.SelectedItem = HeatMinutesCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == minutes.ToString(System.Globalization.CultureInfo.InvariantCulture))
                                            ?? HeatMinutesCombo.Items[1];
            UpdatePawnIoStatus();
        }
    }

    private void OpenTaskbarSettings_Click(object sender, RoutedEventArgs e) => App.OpenSettings(MochiDock.Core.SettingsSections.Taskbar);

    private void UpdatePawnIoStatus()
    {
        bool installed = MochiDock.Services.SensorService.IsPawnIoInstalled;
        PawnIoStatusText.Text = installed
            ? "Installed. Real CPU temperature is available after MochiDock restarts."
            : "Not installed. CPU temperature is an estimate.";
        PawnIoSiteButton.Visibility = PawnIoCopyButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PawnIoSite_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(MochiDock.Services.SensorService.PawnIoUrl) { UseShellExecute = true }); }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("Sensors", ex); }
    }

    private void PawnIoCopy_Click(object sender, RoutedEventArgs e)
    {
        try { System.Windows.Clipboard.SetText(MochiDock.Services.SensorService.PawnIoWingetCommand); PawnIoStatusText.Text = "Copied. Paste it into a terminal, then restart MochiDock."; }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("Sensors", ex); }
    }

    private void PawnIoCheck_Click(object sender, RoutedEventArgs e) => UpdatePawnIoStatus();

    private async void DetectComponents_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.DetectPowerComponentsAsync();
    }

    private void ResetComponents_Click(object sender, RoutedEventArgs e) => ViewModel?.ResetPowerComponents();

    private void RemoveComponent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PowerComponentItem item }) ViewModel?.RemovePowerComponent(item);
    }

    // Enter commits a number box (the binding updates on LostFocus), the same as tabbing out of it.
    private void ComponentBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || sender is not System.Windows.Controls.TextBox box) return;
        box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    private void ComponentName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        AddComponent_Click(sender, e);
        e.Handled = true;
    }

    private void AddComponent_Click(object sender, RoutedEventArgs e)
    {
        var kind = ComponentKindCombo.SelectedItem is ComboBoxItem { Tag: PowerComponentKind chosen } ? chosen : PowerComponentKind.Other;
        ViewModel?.AddPowerComponent(kind, ComponentNameBox.Text);
        ComponentNameBox.Clear();
    }

    private void FillComponentKinds()
    {
        if (ComponentKindCombo.Items.Count > 0) return;
        // Detected kinds (board, memory, drives) are listed too: a second NVMe drive the scan missed can be added by hand.
        foreach (var kind in Enum.GetValues<PowerComponentKind>().Where(k => k != PowerComponentKind.Motherboard))
            ComponentKindCombo.Items.Add(new ComboBoxItem { Content = $"{PowerComponentDefaults.Label(kind)} · ~{PowerComponentDefaults.Watts(kind):0.#} W", Tag = kind });
        ComponentKindCombo.SelectedIndex = 0;
    }

    private void Heat_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        double heat = Math.Round(Math.Clamp(HeatSlider.Value, 60, 95));
        int minutes = int.TryParse((HeatMinutesCombo.SelectedItem as ComboBoxItem)?.Tag as string, out int m) ? m : 10;
        ViewModel?.UpdateSettings(x => { x.SustainedHeatThreshold = heat; x.SustainedHeatMinutes = minutes; });
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.StatsHistory?.Clear();
        HistoryRow.Validation = "History cleared.";
    }

    private void Threshold_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || Loading) return;
        double cpu = SettingsValidation.ClampCpuGpuThreshold(CpuSlider.Value);
        double gpu = SettingsValidation.ClampCpuGpuThreshold(GpuSlider.Value);
        double temp = SettingsValidation.ClampTemperatureThreshold(TempSlider.Value);
        ViewModel?.UpdateSettings(x => { x.CpuAlertThreshold = cpu; x.GpuAlertThreshold = gpu; x.TemperatureAlertThreshold = temp; }, SettingsApply.HardwareAlerts);
    }
}
