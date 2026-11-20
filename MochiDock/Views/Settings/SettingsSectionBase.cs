using System.Windows;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Settings;

/// <summary>
/// Base for settings sections. DataContext is the <see cref="MainViewModel"/>. It reloads the section from the settings when it is shown
/// and whenever <see cref="MainViewModel.SettingsChanged"/> fires, and flags those reloads through <see cref="Loading"/> so change handlers can ignore them.
/// </summary>
public abstract class SettingsSectionBase : UserControl
{
    private MainViewModel? _subscribed;

    protected SettingsSectionBase()
    {
        Loaded += (s, e) => { Subscribe(); Refresh(); };
        Unloaded += (s, e) => Unsubscribe();
        IsVisibleChanged += (s, e) => { if (IsVisible) Refresh(); };
    }

    protected MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>True while the section is being filled from the settings.</summary>
    protected bool Loading { get; private set; }

    /// <summary>Fills the controls from <c>ViewModel.Settings</c>. A box the user is typing in should keep its text.</summary>
    protected virtual void LoadFromSettings() { }

    protected void Refresh()
    {
        if (ViewModel == null) return;
        Loading = true;
        try { LoadFromSettings(); }
        finally { Loading = false; }
    }

    private void Subscribe()
    {
        if (ReferenceEquals(_subscribed, ViewModel)) return;
        Unsubscribe();
        _subscribed = ViewModel;
        if (_subscribed != null) _subscribed.SettingsChanged += OnSettingsChanged;
    }

    private void Unsubscribe()
    {
        if (_subscribed != null) _subscribed.SettingsChanged -= OnSettingsChanged;
        _subscribed = null;
    }

    private void OnSettingsChanged(SettingsApply effects) => Dispatcher.BeginInvoke(Refresh);
}
