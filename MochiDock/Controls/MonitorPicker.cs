using System.Windows;
using System.Windows.Controls;
using MochiDock.Services;
using MochiDock.ViewModels;
using Microsoft.Win32;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Controls;

/// <summary>
/// One button per connected display; the one the dock is on is selected. Shared by the quick settings Appearance card and the Appearance
/// section of Advanced Settings (and mirrored by the tray menu), so they never disagree: every change goes through
/// <see cref="MainViewModel.UpdateSettings"/>, every view rebuilds on <see cref="MainViewModel.SettingsChanged"/>, and the list is rebuilt
/// when displays are plugged in or out. With one display it is shown selected, with a short note.
/// </summary>
public sealed class MonitorPicker : UserControl
{
    private readonly WrapPanel _buttons = new();
    private readonly TextBlock _note = new() { Text = "Only one display connected", Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
    private MainViewModel? _subscribed;
    private bool _displayEventsHooked;

    public MonitorPicker()
    {
        _note.SetResourceReference(TextBlock.ForegroundProperty, "IslandMutedInkBrush");
        _note.SetResourceReference(FrameworkElement.StyleProperty, "CardCaptionText");
        var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(_buttons);
        stack.Children.Add(_note);
        Content = stack;
        HorizontalAlignment = HorizontalAlignment.Left;

        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (IsLoaded) Attach(); };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Attach()
    {
        Detach();
        _subscribed = ViewModel;
        if (_subscribed != null) _subscribed.SettingsChanged += OnSettingsChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        _displayEventsHooked = true;
        Rebuild();
    }

    private void Detach()
    {
        if (_subscribed != null) _subscribed.SettingsChanged -= OnSettingsChanged;
        _subscribed = null;
        if (_displayEventsHooked) SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        _displayEventsHooked = false;
    }

    private void OnSettingsChanged(SettingsApply effects) => Dispatcher.BeginInvoke(Rebuild);
    private void OnDisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Rebuild);

    private void Rebuild()
    {
        _buttons.Children.Clear();
        var monitors = MultiMonitorHelper.GetAllMonitors();
        var target = ViewModel?.Settings.TargetMonitor;
        foreach (var monitor in monitors)
        {
            // An empty target means "the primary display".
            bool current = monitors.Count == 1 || monitor.DeviceName.Equals(target, StringComparison.OrdinalIgnoreCase) || (string.IsNullOrEmpty(target) && monitor.IsPrimary);
            var label = monitor.DisplayLabel.Split(':')[0].Trim() + (monitors.Count > 1 && monitor.IsPrimary ? " · Primary" : "");
            var button = DisplayMonitorButton.Create(label, current);
            button.Margin = new Thickness(0, 0, 8, 6);
            button.Click += (_, _) =>
            {
                // The primary display is stored as "" so the choice follows the primary if the app moves to another PC.
                string device = monitor.IsPrimary ? "" : monitor.DeviceName;
                ViewModel?.UpdateSettings(x => x.TargetMonitor = device, SettingsApply.Reposition);
            };
            _buttons.Children.Add(button);
        }
        _note.Visibility = monitors.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
    }
}
