using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MochiDock.Core;
using MochiDock.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Settings.Sections;

/// <summary>Email settings: ad filter and the extra Outlook folders to watch. DataContext is the view model.</summary>
public partial class EmailSectionView : UserControl
{
    private bool _loading;
    private MainViewModel? _subscribed;
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private const string OtherProvider = "Other";
    private sealed record ImapRow(string Address, string Status);

    public EmailSectionView()
    {
        InitializeComponent();
        ProviderCombo.ItemsSource = ImapAccountPolicy.Presets.Select(p => p.Name).Append(OtherProvider).ToList();
        ProviderCombo.SelectedIndex = 0;
        Loaded += (s, e) => { Subscribe(); LoadFromSettings(); _statusTimer.Start(); };
        Unloaded += (s, e) => { Unsubscribe(); _statusTimer.Stop(); };
        _statusTimer.Tick += (s, e) => LoadImapList();
        IsVisibleChanged += (s, e) => { if (IsVisible) LoadFromSettings(); };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

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

    private void OnSettingsChanged(SettingsApply effects) => Dispatcher.BeginInvoke(LoadFromSettings);

    private void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        _loading = true;
        try
        {
            FilterAdsCheck.IsChecked = vm.Settings.FilterAds;
            OnlyListedCheck.IsChecked = vm.Settings.OutlookOnlyListedFolders;
            var folders = vm.Settings.OutlookTrackedFolders ?? new();
            OnlyListedRow.Validation = vm.Settings.OutlookOnlyListedFolders && folders.Count == 0
                ? "No folders are listed, so no mail is watched. Add a folder below or turn this off." : "";
            FolderList.ItemsSource = folders.ToList();
            LoadImapList();
            NoFoldersText.Visibility = folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _loading = false; }
    }

    private void LoadImapList()
    {
        var vm = ViewModel;
        if (vm == null) return;
        var rows = vm.Settings.ImapAccounts.Select(a => new ImapRow(a.Address, vm.ImapMail?.StatusOf(a.Address) is { Length: > 0 } s ? s : "Starting...")).ToList();
        ImapList.ItemsSource = rows;
        NoImapText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private ImapPreset? SelectedPreset => ImapAccountPolicy.Presets.FirstOrDefault(p => p.Name == ProviderCombo.SelectedItem as string);

    private void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (HostRow == null) return;
        var preset = SelectedPreset;
        HostRow.Visibility = preset == null ? Visibility.Visible : Visibility.Collapsed;
        HelpButton.Visibility = preset == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPreset is { } preset)
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(preset.HelpUrl) { UseShellExecute = true }); }
            catch (Exception ex) { MochiDock.Services.AppLog.Warn("Mail", ex); }
    }

    private async void AddImap_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var preset = SelectedPreset;
        string host = preset?.Host ?? HostBox.Text;
        int port = preset?.Port ?? (int.TryParse(PortBox.Text.Trim(), out int p) ? p : 0);
        var account = new ImapAccount { Address = AddressBox.Text, Host = host, Port = port, MarkAsRead = MarkReadCheck.IsChecked == true };
        AddImapButton.IsEnabled = false;
        ImapMessageText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "IslandSecondaryInkBrush");
        ImapMessageText.Text = "Checking the login...";
        string problem;
        try { problem = await vm.AddImapAccountAsync(account, ImapPasswordBox.Password); }
        finally { AddImapButton.IsEnabled = true; }
        if (problem.Length == 0)
        {
            ImapPasswordBox.Clear();
            AddressBox.Clear();
            ImapMessageText.Text = "Account added. New mail will appear in the dock.";
        }
        else
        {
            ImapMessageText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "IslandWarningBrush");
            ImapMessageText.Text = problem;
        }
    }

    private void RemoveImap_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string address }) ViewModel?.RemoveImapAccount(address);
    }

    private void FilterAds_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _loading) return;
        bool filter = FilterAdsCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => x.FilterAds = filter, SettingsApply.MailFilter);
    }

    private void OnlyListed_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _loading) return;
        bool only = OnlyListedCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => x.OutlookOnlyListedFolders = only, SettingsApply.OutlookFolders);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e) => AddFolder();

    private void AddFolderBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        AddFolder();
        e.Handled = true;
    }

    private void AddFolder()
    {
        if (ViewModel == null) return;
        AddRow.Validation = "";
        if (string.IsNullOrWhiteSpace(AddFolderBox.Text)) return;
        if (!SettingsValidation.TryAddOutlookFolders(ViewModel.Settings.OutlookTrackedFolders, AddFolderBox.Text, out var folders, out var error))
        {
            AddRow.Validation = error ?? "";
            return;
        }
        AddFolderBox.Clear();
        ViewModel.UpdateSettings(x => x.OutlookTrackedFolders = folders, SettingsApply.OutlookFolders);
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null || sender is not FrameworkElement { Tag: string name }) return;
        var folders = (ViewModel.Settings.OutlookTrackedFolders ?? new())
            .Where(f => !string.Equals(f, name, StringComparison.OrdinalIgnoreCase)).ToList();
        ViewModel.UpdateSettings(x => x.OutlookTrackedFolders = folders, SettingsApply.OutlookFolders);
    }
}
