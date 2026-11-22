using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MochiDock.Core;
using MochiDock.Services;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace MochiDock.Views.Settings.Sections;

/// <summary>One line of the muted or notify list: the id that is saved and the name to show.</summary>
public sealed record ChatListEntry(string Key, string Display);

/// <summary>One row of the chat picker. <see cref="Listed"/> is the checkbox: silenced, or (with "only listed" on for its kind) notifying.</summary>
public sealed class ChatPickRow
{
    public WhatsAppChatEntry Chat { get; init; } = null!;
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Action { get; init; } = "";
    public bool Listed { get; set; }
}

/// <summary>WhatsApp (GOWA) settings. All writes go through <see cref="MainViewModel.UpdateSettings"/>; DataContext is the view model.</summary>
public partial class WhatsAppSectionView : SettingsSectionBase
{
    private int _readSyncRequest;
    // The read-sync line ("Last read receipt: 14:05 (3 min ago)") is refreshed every 15 s while the page is open.
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private void UpdateReadSyncStatus() { if (ViewModel is { } vm) ReadSyncStatusText.Text = vm.ReadSyncStatus(); }
    private readonly System.Windows.Threading.DispatcherTimer _linkTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private void UpdateLinkStatus()
    {
        if (ViewModel is not { } vm) return;
        LinkStatusText.Text = vm.WhatsAppLinkStatus;
        LinkStepsText.Visibility = vm.Settings.EnableWhatsApp && !vm.WhatsAppIsLinked ? Visibility.Visible : Visibility.Collapsed;
    }
    private IReadOnlyList<WhatsAppChatEntry> _pickerChats = Array.Empty<WhatsAppChatEntry>();
    private string _pickerMode = "";
    private int _pickerRequest;

    public WhatsAppSectionView()
    {
        InitializeComponent();
        Loaded += (s, e) => { _statusTimer.Start(); _linkTimer.Start(); UpdateReadSyncStatus(); UpdateLinkStatus(); };
        Unloaded += (s, e) => { _statusTimer.Stop(); _linkTimer.Stop(); };
        _linkTimer.Tick += (s, e) => UpdateLinkStatus();
        _statusTimer.Tick += (s, e) => UpdateReadSyncStatus();
    }

    // A box being edited keeps its text; everything else follows the settings.
    private static void SetText(TextBox box, string text) { if (!box.IsKeyboardFocused) box.Text = text; }

    protected override void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        var settings = vm.Settings;
        {
            EnableWhatsAppCheck.IsChecked = settings.EnableWhatsApp;
            WhatsAppBody.IsEnabled = settings.EnableWhatsApp;
            WhatsAppBody.Opacity = settings.EnableWhatsApp ? 1 : 0.45;
            bool remote = string.Equals(settings.GowaMode, "Remote", StringComparison.OrdinalIgnoreCase);
            LocalModeRadio.IsChecked = !remote;
            RemoteModeRadio.IsChecked = remote;
            SetText(LocalPortBox, (settings.GowaPort > 0 ? settings.GowaPort : 3099).ToString());
            SetText(WebhookPortBox, (settings.WebhookPort > 0 ? settings.WebhookPort : 47823).ToString());
            SetText(RemoteUrlBox, settings.RemoteGowaUrl ?? "");
            var credentials = GowaCredentialStore.Load();
            SetText(RemoteUserBox, credentials?.User ?? "");
            if (!RemotePasswordBox.IsKeyboardFocused) RemotePasswordBox.Password = credentials?.Password ?? "";
            IgnoreArchivedCheck.IsChecked = settings.IgnoreArchivedChats;
            MuteGroupsCheck.IsChecked = settings.MuteAllWhatsAppGroups;

            var names = settings.WhatsAppChatNames;
            var muted = settings.MutedWhatsAppChatJids ?? new();
            MutedList.ItemsSource = muted.Select(k => new ChatListEntry(k, WhatsAppChatList.EntryLabel(k, names))).ToList();
            NoMutedText.Visibility = muted.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            OnlyListedCheck.IsChecked = settings.WhatsAppOnlyListedGroups;
            var allowed = settings.WhatsAppAllowedGroups ?? new();
            AllowedList.ItemsSource = allowed.Select(k => new ChatListEntry(k, WhatsAppChatList.EntryLabel(k, names))).ToList();
            OnlyPeopleCheck.IsChecked = settings.WhatsAppOnlyListedPeople;
            bool anyOnly = settings.WhatsAppOnlyListedGroups || settings.WhatsAppOnlyListedPeople;
            AllowedRow.Visibility = anyOnly ? Visibility.Visible : Visibility.Collapsed;
            AllowedWarning.Visibility = anyOnly && allowed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            // What a tick means changes with those two switches, so an open picker redraws (only then: a redraw loses the scroll position).
            string pickerMode = $"{settings.WhatsAppOnlyListedGroups}/{settings.WhatsAppOnlyListedPeople}";
            if (pickerMode != _pickerMode) { _pickerMode = pickerMode; if (PickerPanel.Visibility == Visibility.Visible) RebuildPicker(); }

            UpdateModeVisibility(remote);
            UpdateRemoteWarning();
            UpdateLinkStatus();
        }
        _ = RefreshReadSyncUrlAsync();
    }

    private void UpdateModeVisibility(bool remote)
    {
        LocalPortRow.Visibility = remote ? Visibility.Collapsed : Visibility.Visible;
        foreach (var row in new FrameworkElement[] { RemoteUrlRow, RemoteUserRow, RemotePasswordRow, ReadSyncRow, ReadSyncStatusText })
            row.Visibility = remote ? Visibility.Visible : Visibility.Collapsed;
        if (!remote) RemoteWarningText.Visibility = Visibility.Collapsed;
    }

    private void LocalModeRadio_Checked(object sender, RoutedEventArgs e) => ModeChosen("Local");
    private void RemoteModeRadio_Checked(object sender, RoutedEventArgs e) => ModeChosen("Remote");

    private void ModeChosen(string mode)
    {
        if (!IsLoaded || Loading) return;
        UpdateModeVisibility(mode == "Remote");
        CommitGowaConfig(mode);
    }

    private void GowaField_LostFocus(object sender, RoutedEventArgs e) => CommitGowaConfig();

    private void GowaField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitGowaConfig();
        e.Handled = true;
    }

    private void CommitGowaConfig(string? mode = null)
    {
        if (!IsLoaded || Loading || ViewModel == null) return;
        var settings = ViewModel.Settings;
        bool changed = false;

        int newPort = settings.GowaPort;
        LocalPortRow.Validation = "";
        if (SettingsValidation.TryParsePort(LocalPortBox.Text, out int port)) { changed |= port != settings.GowaPort; newPort = port; }
        else LocalPortRow.Validation = $"Enter a port between {SettingsValidation.PortMin} and {SettingsValidation.PortMax}. Keeping {settings.GowaPort}.";

        int newWebhook = settings.WebhookPort;
        WebhookPortRow.Validation = "";
        if (SettingsValidation.TryParsePort(WebhookPortBox.Text, out int webhook)) { changed |= webhook != settings.WebhookPort; newWebhook = webhook; }
        else WebhookPortRow.Validation = $"Enter a port between {SettingsValidation.PortMin} and {SettingsValidation.PortMax}. Keeping {settings.WebhookPort}.";

        string newMode = mode ?? settings.GowaMode;
        changed |= !string.Equals(settings.GowaMode, newMode, StringComparison.Ordinal);

        string newRemote = settings.RemoteGowaUrl;
        string remote = SettingsValidation.TrimRemoteUrl(RemoteUrlBox.Text);
        if (!string.IsNullOrWhiteSpace(remote) && !string.Equals(remote, settings.RemoteGowaUrl, StringComparison.OrdinalIgnoreCase))
        {
            newRemote = remote;
            changed = true;
        }

        // The server login lives outside settings.json (DPAPI).
        var stored = GowaCredentialStore.Load();
        string user = RemoteUserBox.Text.Trim();
        string password = RemotePasswordBox.Password;
        if (!string.Equals(stored?.User ?? "", user, StringComparison.Ordinal) || !string.Equals(stored?.Password ?? "", password, StringComparison.Ordinal))
        {
            if (user.Length == 0) GowaCredentialStore.Clear(); else GowaCredentialStore.Save(user, password);
            changed = true;
        }
        UpdateRemoteWarning();

        if (!changed) return;
        ViewModel.UpdateSettings(x => { x.GowaPort = newPort; x.WebhookPort = newWebhook; x.GowaMode = newMode; x.RemoteGowaUrl = newRemote; }, SettingsApply.Gowa);
    }

    private void UpdateRemoteWarning()
    {
        string url = RemoteUrlBox.Text.Trim();
        string? warning = null;
        if (SettingsValidation.ShouldWarnPublicAddress(url))
            warning = "This address is not on a private network or Tailscale. Do not expose GOWA to the internet.";
        else if (RemoteUserBox.Text.Trim().Length == 0)
            warning = "No login set: anyone who can reach this address can read your WhatsApp. Start the server with --basic-auth.";
        RemoteWarningText.Text = warning ?? "";
        RemoteWarningText.Visibility = warning == null || RemoteModeRadio.IsChecked != true ? Visibility.Collapsed : Visibility.Visible;
    }

    // The lookup resolves DNS, so it runs off the UI thread; only the newest request may update the box.
    private async System.Threading.Tasks.Task RefreshReadSyncUrlAsync()
    {
        var vm = ViewModel;
        if (vm == null) return;
        int request = ++_readSyncRequest;
        string url = "";
        try { url = await vm.GetReadSyncUrlAsync(); }
        catch (Exception ex) { AppLog.Warn("Settings", ex); }
        if (request == _readSyncRequest) ReadSyncUrlBox.Text = url;
    }

    private void CopyReadSync_Click(object sender, RoutedEventArgs e)
    {
        if (ReadSyncUrlBox.Text.Length == 0) return;
        try { System.Windows.Clipboard.SetText(ReadSyncUrlBox.Text); }
        catch (Exception ex) { AppLog.Warn("Settings", ex); } // the clipboard can be locked by another app
    }

    // A new secret invalidates the old address at once; the GOWA server must be given the new one.
    private void RegenerateToken_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var answer = System.Windows.MessageBox.Show(Window.GetWindow(this),
            "Create a new secret? The current read-sync address stops working until you give the new address to the GOWA server.",
            "Regenerate read-sync secret", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;
        string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        ViewModel.UpdateSettings(x => x.RemoteWebhookToken = token, SettingsApply.Gowa);
        _ = RefreshReadSyncUrlAsync();
    }

    private async void EnableWhatsApp_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        bool on = EnableWhatsAppCheck.IsChecked == true;
        if (on && !string.Equals(ViewModel?.Settings.GowaMode, "Remote", StringComparison.OrdinalIgnoreCase) &&
            GowaInstaller.ResolveLocalExecutable() == null)
        {
            await DownloadLocalEngineWithFeedbackAsync();
        }
        ViewModel?.UpdateSettings(x => x.EnableWhatsApp = on, SettingsApply.Gowa);
    }

    private async Task DownloadLocalEngineWithFeedbackAsync()
    {
        LinkStatusText.Text = "Downloading local WhatsApp engine… 0%";
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            int pct = p.Total > 0 ? (int)(p.Done * 100 / p.Total) : 0;
            Dispatcher.BeginInvoke(() => LinkStatusText.Text = $"Downloading local WhatsApp engine… {pct}%");
        });
        try
        {
            await GowaInstaller.EnsureInstalledAsync(progress);
            LinkStatusText.Text = "Local WhatsApp engine downloaded. Starting…";
        }
        catch (Exception ex)
        {
            AppLog.Warn("Gowa", ex);
            LinkStatusText.Text = $"Could not download WhatsApp engine: {ex.Message}";
        }
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        bool ignoreArchived = IgnoreArchivedCheck.IsChecked == true;
        bool muteGroups = MuteGroupsCheck.IsChecked == true;
        bool onlyListed = OnlyListedCheck.IsChecked == true;
        bool onlyPeople = OnlyPeopleCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => { x.IgnoreArchivedChats = ignoreArchived; x.MuteAllWhatsAppGroups = muteGroups; x.WhatsAppOnlyListedGroups = onlyListed; x.WhatsAppOnlyListedPeople = onlyPeople; }, SettingsApply.Gowa);
    }

    private void UnmuteChat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string jid }) ViewModel?.UnmuteWhatsAppChat(jid);
    }

    private void RemoveAllowed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string entry }) ViewModel?.RemoveAllowedWhatsAppGroup(entry);
    }

    // ---- Chat picker: the server's chat list, ticked once instead of waiting for a popup from each chat ----

    private async void OpenPicker_Click(object sender, RoutedEventArgs e)
    {
        if (PickerPanel.Visibility == Visibility.Visible) { PickerPanel.Visibility = Visibility.Collapsed; return; }
        PickerPanel.Visibility = Visibility.Visible;
        await LoadPickerAsync();
    }

    private async System.Threading.Tasks.Task LoadPickerAsync()
    {
        var vm = ViewModel;
        if (vm == null) return;
        int request = ++_pickerRequest;
        PickerStatus.Text = "Loading your chats…";
        IReadOnlyList<WhatsAppChatEntry> chats = Array.Empty<WhatsAppChatEntry>();
        try { chats = await vm.LoadWhatsAppChatsAsync(); }
        catch (Exception ex) { AppLog.Warn("Settings", ex); }
        if (request != _pickerRequest) return;
        _pickerChats = chats;
        RebuildPicker();
    }

    private WhatsAppChatKind PickerKind => PickGroups.IsChecked == true ? WhatsAppChatKind.Groups : PickPeople.IsChecked == true ? WhatsAppChatKind.People : WhatsAppChatKind.All;

    private IReadOnlyList<WhatsAppChatEntry> ShownChats() => WhatsAppChatList.Filter(_pickerChats, PickerSearch.Text, PickerKind);

    private void RebuildPicker()
    {
        var vm = ViewModel;
        if (vm == null) return;
        bool severalAccounts = _pickerChats.Select(c => c.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        var rows = ShownChats().Select(chat =>
        {
            var parts = new List<string> { chat.IsGroup ? "Group" : "Person" };
            if (severalAccounts && chat.AccountName.Length > 0) parts.Add(chat.AccountName);
            if (chat.LastMessageUtc is { } last) parts.Add(Ago(last));
            if (chat.Archived) parts.Add("archived");
            return new ChatPickRow
            {
                Chat = chat,
                Title = WhatsAppChatList.DisplayName(chat),
                Detail = string.Join(" · ", parts),
                Action = vm.WhatsAppPickerUsesNotifyList(chat.IsGroup) ? "notify" : "silence",
                Listed = vm.IsWhatsAppChatListed(chat),
            };
        }).ToList();
        PickerList.ItemsSource = rows;
        PickerStatus.Text = _pickerChats.Count == 0
            ? "No chats came back. Check that the server is connected (see Connection below), then press Reload."
            : $"{rows.Count} of {_pickerChats.Count} chats. A tick means silence, or notify for a kind that is set to only notify for listed chats.";
    }

    private static string Ago(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        if (age < TimeSpan.FromHours(1)) return Math.Max(1, (int)age.TotalMinutes) + " min ago";
        if (age < TimeSpan.FromDays(1)) return (int)age.TotalHours + " h ago";
        return (int)age.TotalDays + " d ago";
    }

    private void PickerSearch_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded && _pickerChats.Count > 0) RebuildPicker(); }
    private void PickerKind_Changed(object sender, RoutedEventArgs e) { if (IsLoaded && _pickerChats.Count > 0) RebuildPicker(); }
    private async void PickerReload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button) await MochiDock.Controls.RefreshFeedback.RunAsync(button, LoadPickerAsync);
        else await LoadPickerAsync();
    }
    private void PickerClose_Click(object sender, RoutedEventArgs e) => PickerPanel.Visibility = Visibility.Collapsed;

    private void PickerChat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChatPickRow row }) ViewModel?.SetWhatsAppChatsListed(new[] { row.Chat }, row.Listed);
    }

    private void PickerTickAll_Click(object sender, RoutedEventArgs e) => TickShown(true);
    private void PickerClearAll_Click(object sender, RoutedEventArgs e) => TickShown(false);

    private void TickShown(bool listed)
    {
        ViewModel?.SetWhatsAppChatsListed(ShownChats(), listed);
        RebuildPicker();
    }

    private void OpenQr_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenWhatsAppDashboard();

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is System.Windows.Controls.Button button) await MochiDock.Controls.RefreshFeedback.RunAsync(button, vm.RetryIntegrations);
    }
}
