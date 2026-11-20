using System;
using System.Windows;
using MochiDock.Services;

namespace MochiDock.Views.Settings.Sections;

/// <summary>
/// Calendar feed link (kept in the DPAPI store by <see cref="CalendarFeedService"/>) and calendar display options.
/// The saved link is never shown; while one is saved the section shows "Linked" with Refresh, Change and Disconnect.
/// </summary>
public partial class CalendarSectionView : SettingsSectionBase
{
    private CalendarFeedService? _calendar;
    private bool _changing;

    public CalendarSectionView()
    {
        InitializeComponent();
        Unloaded += (s, e) => Detach();
    }

    protected override void LoadFromSettings()
    {
        var service = ViewModel?.CalendarFeed;
        if (!ReferenceEquals(service, _calendar))
        {
            Detach();
            _calendar = service;
            if (_calendar != null) _calendar.Changed += OnCalendarChanged;
        }
        RefreshStatus();
    }

    private void Detach()
    {
        if (_calendar != null) _calendar.Changed -= OnCalendarChanged;
        _calendar = null;
    }

    private void OnCalendarChanged() => Dispatcher.BeginInvoke(RefreshStatus);

    private void RefreshStatus()
    {
        bool linked = _calendar?.IsConnected == true;
        bool showEntry = !linked || _changing;
        LinkedPanel.Visibility = linked && !_changing ? Visibility.Visible : Visibility.Collapsed;
        EntryPanel.Visibility = showEntry ? Visibility.Visible : Visibility.Collapsed;
        CancelBtn.Visibility = linked && _changing ? Visibility.Visible : Visibility.Collapsed;
        ConnectBtn.IsEnabled = _calendar != null && LinkBox.Password.Trim().Length > 0;
        LinkBox.IsEnabled = _calendar != null;

        if (_calendar == null)
        {
            StatusText.Text = "Calendar unavailable";
            return;
        }
        bool refreshing = _calendar.IsRefreshing;
        RefreshBtn.IsEnabled = !refreshing;
        RefreshBtn.Content = refreshing ? "Refreshing…" : "Refresh now";
        StatusText.Text = _calendar.StatusText;
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            _calendar.State == CalendarFeedState.Invalid ? "IslandNegativeBrush" : _calendar.State == CalendarFeedState.Updated ? "IslandPositiveBrush" : "IslandMutedInkBrush");
    }

    private void LinkBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        LinkRow.Validation = "";
        ConnectBtn.IsEnabled = _calendar != null && LinkBox.Password.Trim().Length > 0;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_calendar == null || LinkBox.Password.Trim().Length == 0) return;
        LinkRow.Validation = "";
        string error = _calendar.Connect(LinkBox.Password);
        if (error.Length > 0)
        {
            LinkRow.Validation = error;
            return;
        }
        LinkBox.Clear(); // the link is a secret: it is not kept in the box once saved
        _changing = false;
        RefreshStatus();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_calendar == null) return;
        // The shared helper disables the button, spins its glyph and keeps it busy long enough to notice; the status line is then rebuilt as before.
        await MochiDock.Controls.RefreshFeedback.RunAsync(RefreshBtn, async () => await _calendar.RefreshAsync(force: true));
        RefreshStatus();
    }

    private void ChangeLink_Click(object sender, RoutedEventArgs e)
    {
        _changing = true;
        LinkRow.Validation = "";
        RefreshStatus();
        LinkBox.Focus();
    }

    private void CancelChange_Click(object sender, RoutedEventArgs e)
    {
        _changing = false;
        LinkBox.Clear();
        LinkRow.Validation = "";
        RefreshStatus();
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        _calendar?.Disconnect();
        _changing = false;
        LinkBox.Clear();
        LinkRow.Validation = "";
        RefreshStatus();
    }

    private void OpenNotifications_Click(object sender, RoutedEventArgs e) => App.OpenSettings(MochiDock.Core.SettingsSections.Notifications);
}
