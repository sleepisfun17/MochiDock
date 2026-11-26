using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MochiDock.Core;
using MochiDock.Models;
using MochiDock.Services;

namespace MochiDock.ViewModels;

/// <summary>Live effects that <see cref="MainViewModel.UpdateSettings"/> can run after a settings change.</summary>
[Flags]
public enum SettingsApply
{
    None = 0,
    /// <summary>Reconfigure GOWA (restarts it when mode, address, port or credentials changed).</summary>
    Gowa = 1 << 0,
    /// <summary>Push <c>OutlookTrackedFolders</c> to the running Outlook watcher.</summary>
    OutlookFolders = 1 << 1,
    /// <summary>Re-poll weather for the saved city.</summary>
    Weather = 1 << 2,
    /// <summary>Recompute prayer location (city or calculation method) and schedule.</summary>
    PrayerLocation = 1 << 3,
    /// <summary>Re-apply the capsule theme.</summary>
    Theme = 1 << 4,
    /// <summary>Reposition the dock window; also re-evaluates gaming offset and click-through.</summary>
    Reposition = 1 << 5,
    /// <summary>Restart alert tracking so new thresholds are judged from fresh readings.</summary>
    HardwareAlerts = 1 << 6,
    /// <summary>Refresh capsule text and state (notification, weather and prayer pill content).</summary>
    CapsuleRefresh = 1 << 7,
    /// <summary>Apply <c>FilterAds</c> to the running Outlook watcher.</summary>
    MailFilter = 1 << 8,
    /// <summary>Register or unregister the global resolution hotkey to match <c>EnableResolutionHotkey</c>.</summary>
    Hotkey = 1 << 9,
    /// <summary>Start or stop the event and task reminders (and the calendar's background refresh).</summary>
    Reminders = 1 << 10,
    /// <summary>Re-apply the interface size (zoom) to the dock and the Advanced Settings window.</summary>
    Scale = 1 << 11,
    /// <summary>Give the IMAP mail service the saved account list (starts, stops or restarts its watchers).</summary>
    MailAccounts = 1 << 12,
}

public partial class MainViewModel
{
    /// <summary>Raised after <see cref="UpdateSettings"/> saved a change and ran its effects.</summary>
    public event Action<SettingsApply>? SettingsChanged;

    /// <summary>Raised when a settings page asks the dock window to switch the display resolution (width, height, notice text).</summary>
    public event Action<int, int, string>? ResolutionPresetRequested;

    private string _resolutionHotkeyProblem = "";

    /// <summary>Why the resolution hotkey is not active (invalid text or taken by another application); empty when fine or off. Set by the dock window.</summary>
    public string ResolutionHotkeyProblem
    {
        get => _resolutionHotkeyProblem;
        set { if (_resolutionHotkeyProblem == value) return; _resolutionHotkeyProblem = value; OnPropertyChanged(); }
    }

    private string _windowSizeShortcutProblem = "";
    /// <summary>Why one or more window-size shortcuts are not active (duplicate, taken by another program, unreadable); empty when all work.</summary>
    public string WindowSizeShortcutProblem
    {
        get => _windowSizeShortcutProblem;
        set { if (_windowSizeShortcutProblem == value) return; _windowSizeShortcutProblem = value; OnPropertyChanged(); }
    }

    public void RequestResolutionPreset(int width, int height, string label) => ResolutionPresetRequested?.Invoke(width, height, label);

    /// <summary>
    /// The one way to change settings at runtime: mutates the single in-memory <see cref="Settings"/> object,
    /// saves it, runs only the requested live effects and raises <see cref="SettingsChanged"/>.
    /// Call on the UI thread. <see cref="ApplyImportedSettings"/> stays for Import and Reload only.
    /// </summary>
    public void UpdateSettings(Action<AppSettings> change, SettingsApply effects = SettingsApply.None)
    {
        ArgumentNullException.ThrowIfNull(change);
        change(_settings);
        SettingsService.Save(_settings);
        PruneSuppressedNotifications();
        RefreshMascotMood();
        _hardwareService.PingHost = MochiDock.Core.SettingsValidation.TryNormalizePingHost(_settings.PingHost, out var pingHost) ? pingHost : "1.1.1.1";

        if (effects.HasFlag(SettingsApply.Theme)) ApplyCapsuleTheme(_settings.CapsuleTheme);
        if (effects.HasFlag(SettingsApply.OutlookFolders)) ApplyOutlookFolders(_settings.OutlookTrackedFolders ?? new());
        if (effects.HasFlag(SettingsApply.MailFilter) && (_mailService as CompositeMailService)?.Find<OutlookMailService>() is { } outlook) outlook.FilterAds = _settings.FilterAds;
        if (effects.HasFlag(SettingsApply.MailFilter) && ImapMail is { } imapFilter) imapFilter.FilterAds = _settings.FilterAds;
        if (effects.HasFlag(SettingsApply.MailAccounts)) ImapMail?.Configure(_settings.ImapAccounts);
        if (effects.HasFlag(SettingsApply.Reminders)) ApplyReminderSettings();
        if (effects.HasFlag(SettingsApply.HardwareAlerts)) _hardwareAlerts.ResetContinuity();
        if (effects.HasFlag(SettingsApply.Gowa)) _ = ApplyGowaSettingsAsync();
        if (effects.HasFlag(SettingsApply.Weather)) _ = RefreshWeatherAsync();
        if (effects.HasFlag(SettingsApply.PrayerLocation)) _ = UpdatePrayerLocationAndScheduleAsync();
        if (effects.HasFlag(SettingsApply.CapsuleRefresh))
        {
            OnPropertyChanged(nameof(CompactCapsuleText));
            OnPropertyChanged(nameof(WeatherCityDisplay));
            OnPropertyChanged(nameof(IsPrayerInMainPill));
            RaiseCapsuleStateChanged();
            if (!effects.HasFlag(SettingsApply.PrayerLocation)) _ = UpdatePrayerScheduleAsync();
        }
        ApplyDateFormat();
        // MainWindow repositions on "Settings" and then re-reads the gaming offset and click-through.
        if (effects.HasFlag(SettingsApply.Reposition)) OnPropertyChanged(nameof(Settings));

        SettingsChanged?.Invoke(effects);
    }

    /// <summary>Applies the saved date format app-wide; views that show dates redraw through <see cref="MochiDock.Core.DateDisplay.Changed"/>.</summary>
    private void ApplyDateFormat()
    {
        if (!MochiDock.Core.DateDisplay.Set(MochiDock.Core.DateDisplay.Parse(_settings.DateFormat))) return;
        OnPropertyChanged(nameof(CurrentDateText));
        OnPropertyChanged(nameof(HeaderDateTimeText));
        _ = UpdatePrayerScheduleAsync();   // the "next prayer" day line
    }

    /// <summary>The IMAP watcher, or null when the mail service is not the composite (tests).</summary>
    public ImapMailService? ImapMail => (_mailService as CompositeMailService)?.Find<ImapMailService>();

    /// <summary>
    /// Signs in once to prove the address and app password work, then saves the account (settings) and the password (encrypted store).
    /// Returns an empty string on success, otherwise the reason to show.
    /// </summary>
    public async Task<string> AddImapAccountAsync(MochiDock.Core.ImapAccount account, string password)
    {
        string problem = MochiDock.Core.ImapAccountPolicy.Validate(account.Address, account.Host, account.Port);
        if (problem.Length > 0) return problem;
        if (string.IsNullOrWhiteSpace(password)) return "Enter the app password.";
        if (ImapMail is not { } imap) return "Email is not available.";
        account.Address = account.Address.Trim();
        account.Host = account.Host.Trim();
        string failure = await imap.TestAsync(account, password.Trim(), CancellationToken.None);
        if (failure.Length > 0) return failure;
        try { MailCredentialStore.Default.Save(account.Address, password.Trim()); }
        catch (Exception ex) { return "Could not save the app password: " + ex.Message; }
        UpdateSettings(x =>
        {
            x.ImapAccounts.RemoveAll(a => MochiDock.Core.ImapAccountPolicy.SameAccount(a, account.Address));
            x.ImapAccounts.Add(account);
        }, SettingsApply.MailAccounts);
        return "";
    }

    public void RemoveImapAccount(string address)
    {
        MailCredentialStore.Default.Remove(address);
        UpdateSettings(x => x.ImapAccounts.RemoveAll(a => MochiDock.Core.ImapAccountPolicy.SameAccount(a, address)), SettingsApply.MailAccounts);
    }

    private async Task ApplyGowaSettingsAsync()
    {
        bool changed = _gowaService.Enabled != _settings.EnableWhatsApp ||
                       _gowaService.UseRemoteServer != string.Equals(_settings.GowaMode, "Remote", StringComparison.OrdinalIgnoreCase) ||
                       !string.Equals(_gowaService.RemoteServerUrl, _settings.RemoteGowaUrl, StringComparison.OrdinalIgnoreCase) ||
                       _gowaService.GowaPort != _settings.GowaPort ||
                       _gowaService.WebhookPort != _settings.WebhookPort ||
                       _gowaService.RemoteCredentialsChanged;
        _gowaService.ConfigureFromSettings(_settings);
        if (changed)
        {
            // Resumes on the UI thread (no ConfigureAwait(false)), so IntegrationStatus below is raised there.
            await _gowaService.StartAsync();
            await Task.Delay(1200);
        }
        await _gowaService.CheckConnectionAsync();
        OnPropertyChanged(nameof(IntegrationStatus));
    }

    /// <summary>The linked WhatsApp accounts (device id and own label) for the alias boxes in Settings; empty while none is linked.</summary>
    public IReadOnlyList<(string DeviceId, string OwnLabel)> WhatsAppAccounts => _gowaService.LinkedAccounts;

    /// <summary>Sets (or, when empty, removes) the short name of a linked WhatsApp account and pushes it to the running GOWA service.</summary>
    public void SetWhatsAppAccountAlias(string deviceId, string alias)
    {
        string value = WhatsAppAccountAlias.Normalize(alias);
        string? current = WhatsAppAccountAlias.Find(_settings.WhatsAppAccountAliases, deviceId);
        if (string.Equals(current ?? "", value, StringComparison.Ordinal)) return;
        UpdateSettings(x =>
        {
            x.WhatsAppAccountAliases ??= new(StringComparer.OrdinalIgnoreCase);
            foreach (var key in x.WhatsAppAccountAliases.Keys.Where(k => string.Equals(k, deviceId, StringComparison.OrdinalIgnoreCase)).ToList())
                x.WhatsAppAccountAliases.Remove(key);
            if (value.Length > 0) x.WhatsAppAccountAliases[deviceId] = value;
        }, SettingsApply.Gowa);
    }

    /// <summary>Removes a group from the notify list and pushes the list to the running GOWA service.</summary>
    public void RemoveAllowedWhatsAppGroup(string entry) =>
        UpdateSettings(x => x.WhatsAppAllowedGroups.RemoveAll(j => string.Equals(j, entry, StringComparison.OrdinalIgnoreCase)), SettingsApply.Gowa);

    /// <summary>The chats of every linked WhatsApp account, for the settings chat picker. Empty when the server cannot be reached.</summary>
    /// <summary>Restarts only WhatsApp with the current settings (after the local engine was downloaded from Settings).</summary>
    public async Task RestartWhatsAppAsync()
    {
        _gowaService.ConfigureFromSettings(_settings);
        await _gowaService.StartAsync();
        await Task.Delay(1200);
        await _gowaService.CheckConnectionAsync();
        OnPropertyChanged(nameof(IntegrationStatus));
    }

    public Task<IReadOnlyList<WhatsAppChatEntry>> LoadWhatsAppChatsAsync() => _gowaService.FetchChatListAsync();

    /// <summary>True when "only notify for listed" is on for this kind of chat, so the picker edits the notify list instead of the muted list.</summary>
    public bool WhatsAppPickerUsesNotifyList(bool isGroup) => isGroup ? _settings.WhatsAppOnlyListedGroups : _settings.WhatsAppOnlyListedPeople;

    /// <summary>True when the chat is on the list its picker tick edits.</summary>
    public bool IsWhatsAppChatListed(WhatsAppChatEntry chat) =>
        GowaAccountPolicy.IsMuted(WhatsAppPickerUsesNotifyList(chat.IsGroup) ? _settings.WhatsAppAllowedGroups : _settings.MutedWhatsAppChatJids, chat.Jid, chat.DeviceId);

    /// <summary>Puts chats on (or takes them off) the list their picker tick edits, remembers their names, and pushes the lists to the running GOWA service.</summary>
    public void SetWhatsAppChatsListed(IEnumerable<WhatsAppChatEntry> chats, bool listed)
    {
        var picked = chats.ToList();
        if (picked.Count == 0) return;
        UpdateSettings(x =>
        {
            foreach (var chat in picked)
            {
                var list = WhatsAppPickerUsesNotifyList(chat.IsGroup) ? x.WhatsAppAllowedGroups : x.MutedWhatsAppChatJids;
                string key = _gowaService.MuteKeyFor(chat.Jid, chat.DeviceId);
                if (listed)
                {
                    if (!GowaAccountPolicy.IsMuted(list, chat.Jid, chat.DeviceId)) list.Add(key);
                    x.WhatsAppChatNames[chat.Jid] = WhatsAppChatList.DisplayName(chat);
                }
                else
                {
                    // Every entry that covers this chat goes (the same chat can be saved in more than one form).
                    list.RemoveAll(entry => GowaAccountPolicy.IsMuted(new[] { entry }, chat.Jid, chat.DeviceId));
                }
            }
        }, SettingsApply.Gowa);
    }

    /// <summary>Removes a chat from the WhatsApp muted list and pushes the list to the running GOWA service.</summary>
    public void UnmuteWhatsAppChat(string chatJid) =>
        UpdateSettings(x => x.MutedWhatsAppChatJids.RemoveAll(j => string.Equals(j, chatJid, StringComparison.OrdinalIgnoreCase)), SettingsApply.Gowa);

    /// <summary>
    /// The webhook address to give a shared GOWA server, or "" when unknown. Runs the DNS lookup off the UI thread.
    /// </summary>
    public Task<string> GetReadSyncUrlAsync() => Task.Run(() => _gowaService.BuildReadSyncUrl());

    /// <summary>"Last read receipt: 14:05 (3 min ago)" and the number of rejected webhook calls, for the WhatsApp settings.</summary>
    public string ReadSyncStatus() =>
        MochiDock.Core.ReadSyncStatusText.Build(_gowaService.LastReadReceiptAtUtc, _gowaService.LastReceiptAtUtc, _gowaService.RejectedWebhookCount, DateTime.UtcNow);
}
