using System.Collections.ObjectModel;
using MochiDock.Models;
using MochiDock.Services;
using MascotMood = MochiDock.Core.MascotMood;

namespace MochiDock.ViewModels;

public partial class MainViewModel
{
    private EmailNotification? _currentEmail;
    public ObservableCollection<EmailNotification> RecentEmailMessages { get; } = new();
    private WhatsAppNotification? _currentWhatsApp;
    public ObservableCollection<WhatsAppNotification> RecentWhatsAppMessages { get; } = new();
    public ObservableCollection<WhatsAppConversation> ActiveWhatsAppConversations { get; } = new();
    private WhatsAppConversation? _selectedWhatsAppConversation;
    public WhatsAppConversation? SelectedWhatsAppConversation
    {
        get => _selectedWhatsAppConversation;
        set
        {
            if (_selectedWhatsAppConversation != value)
            {
                _selectedWhatsAppConversation = value;
                OnPropertyChanged();
                if (value?.LatestMessage != null)
                {
                    CurrentWhatsApp = value.LatestMessage;
                }
            }
        }
    }
    private DiscordNotification? _currentDiscord;
    public ObservableCollection<DiscordNotification> RecentDiscordMessages { get; } = new();
    private string _replyText = "";

    // ───────────── Multi-Notification Rotation & Priority Queue ─────────────
    private readonly List<IslandCardType> _notificationQueue = new();
    private int _rotationIndex = 0;
    private bool _isNotificationTakeover = false;
    private DateTime _takeoverStartTime = DateTime.MinValue;

    public bool IsNotificationTakeover => _isNotificationTakeover;

    public void StartNotificationTakeover()
    {
        _isNotificationTakeover = true;
        _takeoverStartTime = DateTime.UtcNow;
        RaiseCapsuleStateChanged();
    }

    public void EndNotificationTakeover()
    {
        if (!_isNotificationTakeover) return;
        _isNotificationTakeover = false;
        RaiseCapsuleStateChanged();
    }

    // A message that is muted, snoozed or arrives in quiet hours is still stored (the tab shows it), but it must not enter the notification
    // queue: the queue is what puts "New message" in the capsule and starts the take-over.
    private bool _recordSilently;

    private void SetCurrentSilently(bool silent, Action assign)
    {
        _recordSilently = silent;
        try { assign(); }
        finally { _recordSilently = false; }
    }

    public void EnqueueNotification(IslandCardType type)
    {
        _notificationQueue.Remove(type);
        _notificationQueue.Insert(0, type); // Priority: newest incoming is front
        _rotationIndex = 0;

        bool isGaming = _hardwareService.IsGameRunning() || _wasGaming;
        if (HasActiveMedia && !isGaming)
        {
            StartNotificationTakeover();
        }
        else
        {
            RaiseCapsuleStateChanged();
        }
    }

    public void RemoveFromNotificationQueue(IslandCardType type)
    {
        _notificationQueue.Remove(type);
        if (_rotationIndex >= _notificationQueue.Count)
        {
            _rotationIndex = Math.Max(0, _notificationQueue.Count - 1);
        }
        if (_notificationQueue.Count == 0)
        {
            _isNotificationTakeover = false;
        }
        RaiseCapsuleStateChanged();
    }

    public void AdvanceNotificationRotation()
    {
        if (_notificationQueue.Count <= 1) return;
        _rotationIndex = (_rotationIndex + 1) % _notificationQueue.Count;
        RaiseCapsuleStateChanged();
    }

    public IslandCardType? CurrentRotationItem =>
        _notificationQueue.Count > 0 && _rotationIndex < _notificationQueue.Count
            ? _notificationQueue[_rotationIndex]
            : null;

    public int NotificationCount => _notificationQueue.Count;

    public bool IsNotificationInMainPill =>
        _notificationQueue.Count > 0 && (_isNotificationTakeover || (!HasActiveMedia && !IsStatsShownInCapsule));

    public string RotationPositionText =>
        _notificationQueue.Count > 1 ? $"{_rotationIndex + 1}/{_notificationQueue.Count}" : "";

    public bool ShowRotationPosition =>
        IsNotificationInMainPill && _notificationQueue.Count > 1;

    public bool HasCompanionBubble =>
        _notificationQueue.Count > 0 && (HasActiveMedia || IsStatsShownInCapsule) && !_isNotificationTakeover && !_wasGaming && !_hardwareStats.IsGamingMode;

    public bool HasMultipleActivities =>
        ((HasActiveMedia ? 1 : 0) + _notificationQueue.Count + (IsStatsShownInCapsule ? 1 : 0)) > 1;

    public bool HasSingleNotification => IsNotificationInMainPill;

    public IslandCardType? PrimaryCompanionItem =>
        _notificationQueue.Count > 0 ? _notificationQueue[0] : null;

    public string CompanionBubbleType => PrimaryCompanionItem switch
    {
        IslandCardType.WhatsApp => "WhatsApp",
        IslandCardType.Discord => "Discord",
        IslandCardType.Apps => "Apps",
        IslandCardType.Email => "Email",
        _ => ""
    };

    public string CompanionBubbleBadge => PrimaryCompanionItem switch
    {
        IslandCardType.WhatsApp => "💬",
        IslandCardType.Discord => "🎮",
        IslandCardType.Apps => "🔔",
        IslandCardType.Email => "✉️",
        _ => ""
    };

    public string CompanionBubbleColor => PrimaryCompanionItem switch
    {
        IslandCardType.WhatsApp => "#25D366",
        IslandCardType.Discord => "#5865F2",
        IslandCardType.Apps => "#8E8E98",
        IslandCardType.Email => "#0A84FF",
        _ => "#8E8E98"
    };

    public string CompanionExtraCountText =>
        _notificationQueue.Count > 1 ? $"+{_notificationQueue.Count - 1}" : "";

    public bool HasCompanionExtraCount => _notificationQueue.Count > 1;

    public EmailNotification? CurrentEmail
    {
        get => _currentEmail;
        set
        {
            _currentEmail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MarkReadTooltip));
            OnPropertyChanged(nameof(CanMarkCurrentEmailRead));
            if (value != null) { if (!_recordSilently) EnqueueNotification(IslandCardType.Email); }
            else RemoveFromNotificationQueue(IslandCardType.Email);
        }
    }

    /// <summary>
    /// Mark Read is shown only when it really marks the email read at its source: not for Mailspring (its database is opened read
    /// only) or a mail account without "Mark as read on the server". Dismiss still removes those from the island.
    /// </summary>
    public bool CanMarkCurrentEmailRead => CurrentEmail is { } email && _mailService.CanMarkAsRead(email);

    public string MarkReadTooltip =>
        string.Equals(CurrentEmail?.ClientSource, "Webmail", StringComparison.OrdinalIgnoreCase)
            ? "Mark this email as read on the mail server"
            : $"Mark this email as read in {CurrentEmail?.ClientSource ?? "Outlook"}";

    public WhatsAppNotification? CurrentWhatsApp
    {
        get => _currentWhatsApp;
        set
        {
            _currentWhatsApp = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanAllowCurrentWhatsAppGroup));
            if (value != null) { if (!_recordSilently) EnqueueNotification(IslandCardType.WhatsApp); }
            else RemoveFromNotificationQueue(IslandCardType.WhatsApp);
        }
    }

    public DiscordNotification? CurrentDiscord
    {
        get => _currentDiscord;
        set
        {
            _currentDiscord = value;
            OnPropertyChanged();
            if (value != null) { if (!_recordSilently) EnqueueNotification(IslandCardType.Discord); }
            else RemoveFromNotificationQueue(IslandCardType.Discord);
        }
    }

    public string ReplyText
    {
        get => _replyText;
        set
        {
            if (_replyText != value)
            {
                _replyText = value;
                OnPropertyChanged();
            }
        }
    }

    private void OnEmailReceived(EmailNotification notif)
    {
        App.BeginOnUI(() =>
        {
            // Maintain multi-email history (most recent first, capped at 10)
            RecentEmailMessages.Insert(0, notif);
            while (RecentEmailMessages.Count > 10)
            {
                RecentEmailMessages.RemoveAt(RecentEmailMessages.Count - 1);
            }

            bool emailShown = ShouldShowNotification("Email");
            SetCurrentSilently(!emailShown, () => CurrentEmail = notif);
            if (!emailShown) { MochiDock.Services.AppLog.Warn("Email", "Not shown: email notifications are muted, snoozed or in quiet hours."); return; }
            bool isGaming = _hardwareService.IsGameRunning();

            if (_isWorkstationLocked && _settings.EnableOvernightGreeting)
            {
                // Locked screen: queue silently
                MochiDock.Services.AppLog.Warn("Email", "Queued: the workstation is locked.");
                _lockedEmailCount++;
                _lockedPreviews.Add($"✉️ {notif.Sender}: {notif.Subject}");
                return;
            }

            if (isGaming && GamingDndActive)
            {
                // Gaming DND: queue silently without audio or expansion
                MochiDock.Services.AppLog.Warn("Email", "Queued: a game is running (gaming do-not-disturb).");
                _queuedGamingEmailCount++;
                _queuedGamingPreviews.Add($"✉️ {notif.Sender}: {notif.Subject}");
                return;
            }

            // Normal Desktop state: play sound and show notification
            SnapshotCardBeforeNotification();
            SoundService.PlayNotificationSound();
            ActiveCardType = IslandCardType.Email;
            MascotMood = MascotMood.Excited;

            if (_settings.AutoExpandNotificationsOnArrival)
            {
                IsExpanded = true;
                _autoCollapseTimer.Stop();
                _autoCollapseTimer.Start();
            }
            else
            {
                // Minimal notification mode: a capsule banner that expands on hover
                StartNotificationTakeover();
            }
        });
    }

    /// <summary>Applies the Outlook folders to watch (besides every Inbox) to the running mail service.</summary>
    public void ApplyOutlookFolders(IReadOnlyList<string> names) =>
        (_mailService as CompositeMailService)?.Find<OutlookMailService>()?.UpdateTrackedFolders(names, _settings.OutlookOnlyListedFolders);

    private bool ShouldShowNotification(string app)
    {
        var now = DateTime.Now;
        if (now < _snoozedUntil) return false;
        if (app == "Email" && _settings.MuteEmail) return false;
        if (app == "Discord" && _settings.MuteDiscord) return false;
        if (app == "Apps" && _settings.MuteApps) return false;
        if (app == "WhatsApp" && _settings.MuteWhatsApp) return false;

        if (_settings.QuietHoursEnabled &&
            MochiDock.Core.SettingsValidation.IsQuietTime(now.TimeOfDay, _settings.QuietHoursStart, _settings.QuietHoursEnd))
            return false;
        return true;
    }

    /// <summary>When the active snooze ends; null when notifications are not snoozed (the tray menu shows it).</summary>
    public DateTime? NotificationsSnoozedUntil => _snoozedUntil > DateTime.Now ? _snoozedUntil : null;

    public void SnoozeNotifications(TimeSpan duration)
    {
        _snoozedUntil = DateTime.Now.Add(duration);
        UpdateSettings(x => x.SnoozeUntilTicks = _snoozedUntil.Ticks);
    }

    private void OnGowaMessageReceived(WhatsAppNotification notif)
    {
        App.BeginOnUI(() =>
        {
            // Maintain multi-message history (most recent first, capped at 10)
            RecentWhatsAppMessages.Insert(0, notif);
            while (RecentWhatsAppMessages.Count > 10)
            {
                RecentWhatsAppMessages.RemoveAt(RecentWhatsAppMessages.Count - 1);
            }

            // Per-Conversation Threading
            string chatKey = !string.IsNullOrEmpty(notif.FromJid) ? notif.FromJid : (notif.IsGroup ? notif.GroupName : notif.SenderName);
            var conv = ActiveWhatsAppConversations.FirstOrDefault(c => string.Equals(c.ChatJid, chatKey, StringComparison.OrdinalIgnoreCase) &&
                MochiDock.Core.GowaAccountPolicy.AppliesTo(c.DeviceId, notif.DeviceId));
            if (conv == null)
            {
                conv = new WhatsAppConversation
                {
                    ChatJid = chatKey,
                    DeviceId = notif.DeviceId,
                    Title = notif.IsGroup && !string.IsNullOrWhiteSpace(notif.GroupName) ? notif.GroupName : notif.SenderName,
                    IsGroup = notif.IsGroup,
                    UnreadCount = 0
                };
                ActiveWhatsAppConversations.Add(conv);

                // Fetch recent group history asynchronously on first message if group
                if (notif.IsGroup && !string.IsNullOrEmpty(notif.FromJid))
                {
                    _ = LoadGroupChatHistoryAsync(conv, notif.FromJid, notif.DeviceId);
                }
            }

            conv.UnreadCount++;
            conv.LatestMessage = notif;
            conv.Messages.Insert(0, notif);
            while (conv.Messages.Count > 10)
            {
                conv.Messages.RemoveAt(conv.Messages.Count - 1);
            }

            bool whatsAppShown = ShouldShowNotification("WhatsApp");
            SetCurrentSilently(!whatsAppShown, () => { SelectedWhatsAppConversation = conv; CurrentWhatsApp = notif; });
            ReplyText = "";
            if (!whatsAppShown) return;
            bool isGaming = _hardwareService.IsGameRunning();

            if (_isWorkstationLocked && _settings.EnableOvernightGreeting)
            {
                // Locked screen: queue silently
                _lockedWhatsAppCount++;
                _lockedPreviews.Add($"💬 {notif.Sender}: {notif.Message}");
                return;
            }

            if (isGaming && GamingDndActive && MochiDock.Core.GamingAllowList.AllowsWhatsApp(_settings.GamingAllowList, notif.FromJid, notif.DeviceId))
            {
                // On the "let through while gaming" list: show it in the gaming capsule, nothing else changes
                ShowGamingPassThrough(MochiDock.Core.GamingAllowList.CapsuleText(notif.IsGroup && !string.IsNullOrWhiteSpace(notif.GroupName) ? notif.GroupName : notif.Sender, notif.Message));
                return;
            }

            if (isGaming && GamingDndActive)
            {
                // Gaming DND: queue silently without audio or expansion
                _queuedGamingWhatsAppCount++;
                _queuedGamingPreviews.Add($"💬 {notif.Sender}: {notif.Message}");
                return;
            }

            // Normal Desktop state: play sound and show notification
            SnapshotCardBeforeNotification();
            SoundService.PlayNotificationSound();
            ActiveCardType = IslandCardType.WhatsApp;
            MascotMood = MascotMood.Excited;

            if (_settings.AutoExpandNotificationsOnArrival)
            {
                IsExpanded = true;
                _autoCollapseTimer.Stop();
                _autoCollapseTimer.Start();
            }
            else
            {
                // Minimal notification mode: a capsule banner that expands on hover
                StartNotificationTakeover();
            }
        });
    }

    private async Task LoadGroupChatHistoryAsync(WhatsAppConversation conv, string chatJid, string deviceId)
    {
        try
        {
            var history = await _gowaService.FetchRecentChatHistoryAsync(chatJid, 10, deviceId).ConfigureAwait(false);
            if (history != null && history.Count > 0)
            {
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Merge older messages from history not already in conv.Messages
                    var existingIds = new HashSet<string>(conv.Messages.Select(m => m.RemoteId).Where(id => !string.IsNullOrEmpty(id)));
                    foreach (var histMsg in history)
                    {
                        if (string.IsNullOrEmpty(histMsg.RemoteId) || !existingIds.Contains(histMsg.RemoteId))
                        {
                            conv.Messages.Add(histMsg);
                            existingIds.Add(histMsg.RemoteId);
                        }
                    }
                });
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
    }

    private void OnGowaMessageReadOrDismissed(string chatJid, List<string>? readIds, string deviceId)
    {
        App.BeginOnUI(() =>
        {
            // Normalize targets: strip @s.whatsapp.net, @c.us, @lid, @g.us
            string cleanTarget = chatJid
                .Replace("@s.whatsapp.net", "")
                .Replace("@c.us", "")
                .Replace("@lid", "")
                .Replace("@g.us", "")
                .Trim();

            if (readIds != null && readIds.Count > 0)
            {
                var readSet = new HashSet<string>(readIds, StringComparer.OrdinalIgnoreCase);
                for (int i = RecentWhatsAppMessages.Count - 1; i >= 0; i--)
                {
                    var msg = RecentWhatsAppMessages[i];
                    if (!string.IsNullOrEmpty(msg.RemoteId) && readSet.Contains(msg.RemoteId))
                    {
                        RecentWhatsAppMessages.RemoveAt(i);
                    }
                }
            }
            else
            {
                // Full chat read or user replied to contact: purge all messages from this sender/chat
                for (int i = RecentWhatsAppMessages.Count - 1; i >= 0; i--)
                {
                    var msg = RecentWhatsAppMessages[i];
                    string msgClean = msg.FromJid
                        .Replace("@s.whatsapp.net", "")
                        .Replace("@c.us", "")
                        .Replace("@lid", "")
                        .Replace("@g.us", "")
                        .Trim();

                    // A chat read or answered on one account must not clear the same contact's messages on another account.
                    if (MochiDock.Core.GowaAccountPolicy.AppliesTo(msg.DeviceId, deviceId) &&
                        (string.Equals(msg.FromJid, chatJid, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(cleanTarget) && (msgClean.Contains(cleanTarget) || cleanTarget.Contains(msgClean)))))
                    {
                        RecentWhatsAppMessages.RemoveAt(i);
                    }
                }
            }

            // Also clean up ActiveWhatsAppConversations
            for (int i = ActiveWhatsAppConversations.Count - 1; i >= 0; i--)
            {
                var c = ActiveWhatsAppConversations[i];
                string cClean = c.ChatJid
                    .Replace("@s.whatsapp.net", "")
                    .Replace("@c.us", "")
                    .Replace("@lid", "")
                    .Replace("@g.us", "")
                    .Trim();

                if (MochiDock.Core.GowaAccountPolicy.AppliesTo(c.DeviceId, deviceId) &&
                    (string.Equals(c.ChatJid, chatJid, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(cleanTarget) && (cClean.Contains(cleanTarget) || cleanTarget.Contains(cClean)))) &&
                    !RecentWhatsAppMessages.Any(m => string.Equals(m.FromJid, c.ChatJid, StringComparison.OrdinalIgnoreCase) &&
                        MochiDock.Core.GowaAccountPolicy.AppliesTo(m.DeviceId, c.DeviceId)))
                {
                    ActiveWhatsAppConversations.RemoveAt(i);
                }
            }

            if (SelectedWhatsAppConversation != null && !ActiveWhatsAppConversations.Contains(SelectedWhatsAppConversation))
            {
                SelectedWhatsAppConversation = ActiveWhatsAppConversations.FirstOrDefault();
            }

            if (RecentWhatsAppMessages.Count > 0)
            {
                CurrentWhatsApp = RecentWhatsAppMessages[0];
            }
            else
            {
                // When all messages are cleared/read, dismiss the card automatically
                DismissWhatsApp();
                if (!_isPinned)
                {
                    IsExpanded = false;
                }
            }
        });
    }

    private void OnDiscordMessageReceived(DiscordNotification notif)
    {
        App.BeginOnUI(() =>
        {
            // Maintain multi-message history (most recent first, capped at 10)
            RecentDiscordMessages.Insert(0, notif);
            while (RecentDiscordMessages.Count > 10)
            {
                RecentDiscordMessages.RemoveAt(RecentDiscordMessages.Count - 1);
            }

            bool discordShown = ShouldShowNotification("Discord");
            SetCurrentSilently(!discordShown, () => CurrentDiscord = notif);
            if (!discordShown) return;
            bool isGaming = _hardwareService.IsGameRunning();

            if (_isWorkstationLocked && _settings.EnableOvernightGreeting)
            {
                // Locked screen: queue silently
                _lockedDiscordCount++;
                _lockedPreviews.Add($"🎮 {notif.Sender}: {notif.Message}");
                return;
            }

            if (isGaming && GamingDndActive && MochiDock.Core.GamingAllowList.AllowsDiscord(_settings.GamingAllowList, notif.Sender))
            {
                ShowGamingPassThrough(MochiDock.Core.GamingAllowList.CapsuleText(notif.Sender, notif.Message));
                return;
            }

            if (isGaming && GamingDndActive)
            {
                // Gaming DND: queue silently without audio or expansion
                _queuedGamingDiscordCount++;
                _queuedGamingPreviews.Add($"🎮 {notif.Sender}: {notif.Message}");
                return;
            }

            // Normal Desktop state: play sound and show notification
            SnapshotCardBeforeNotification();
            SoundService.PlayNotificationSound();
            ActiveCardType = IslandCardType.Discord;
            MascotMood = MascotMood.Excited;

            if (_settings.AutoExpandNotificationsOnArrival)
            {
                IsExpanded = true;
                _autoCollapseTimer.Stop();
                _autoCollapseTimer.Start();
            }
            else
            {
                // Minimal notification mode: a capsule banner that expands on hover
                StartNotificationTakeover();
            }
        });
    }

    public async Task SendQuickReactionAsync(string emoji)
    {
        if (CurrentWhatsApp != null)
        {
            string jid = CurrentWhatsApp.FromJid, device = CurrentWhatsApp.DeviceId;
            bool sent = await _gowaService.SendReplyAsync(jid, emoji, device);
            if (sent) await MarkWhatsAppChatReadAsync(jid, device);
            DismissWhatsApp();
        }
    }

    // True when a notification tab was opened from the side companion bubble (one-time visit).
    private bool _isTransientCompanionView;
    private IslandCardType? _cardBeforeNotificationTakeover;

    /// <summary>Clears the one-time side-bubble flag (manual tab click, pin, etc.).</summary>
    public void ClearTransientCompanionView()
    {
        _isTransientCompanionView = false;
        _cardBeforeNotificationTakeover = null;
    }
    public bool IsTransientCompanionView => _isTransientCompanionView;

    private void SnapshotCardBeforeNotification()
    {
        if (ActiveCardType != IslandCardType.Email &&
            ActiveCardType != IslandCardType.WhatsApp &&
            ActiveCardType != IslandCardType.Discord &&
            ActiveCardType != IslandCardType.Apps &&
            ActiveCardType != IslandCardType.Summary)
        {
            _cardBeforeNotificationTakeover = ActiveCardType;
        }
    }

    private IslandCardType ResolveNotificationDismissFallback()
    {
        if (CurrentMedia.IsPlaying) return IslandCardType.Media;
        if (_cardBeforeNotificationTakeover.HasValue && ValidCardAvailable(_cardBeforeNotificationTakeover.Value.ToString()))
        {
            var card = _cardBeforeNotificationTakeover.Value;
            _cardBeforeNotificationTakeover = null;
            return card;
        }
        return Enum.Parse<IslandCardType>(ValidManualCard());
    }

    /// <summary>
    /// Called right before a hover/click expand from the collapsed main bubble.
    /// If the current tab was opened transiently from the side bubble and media is playing, return to Media.
    /// </summary>
    public void PrepareHoverExpand()
    {
        if (IsPrayerArrivalVisible)
        {
            DismissPrayerArrival();
            _isTransientCompanionView = false;
            ActiveCardType = IslandCardType.Prayer;
            return;
        }

        if (IsPrayerReminderVisible)
        {
            _isTransientCompanionView = false;
            ActiveCardType = IslandCardType.Prayer;
            return;
        }

        // If an incoming notification is active in the pill, ensure expanding opens directly to that notification card!
        if (IsNotificationInMainPill && CurrentRotationItem.HasValue)
        {
            _isTransientCompanionView = false;
            ActiveCardType = CurrentRotationItem.Value;
            return;
        }

        _isTransientCompanionView = false;
        ActiveCardType = Enum.Parse<IslandCardType>(ValidManualCard());
    }
    public void OpenCompanionActivity()
    {
        _isTransientCompanionView = _notificationQueue.Count > 0;
        var targetType = PrimaryCompanionItem ?? (_notificationQueue.Count > 0 ? _notificationQueue[0] : (IslandCardType?)null);

        if (targetType == IslandCardType.WhatsApp)
        {
            ShowWhatsApp(false);
        }
        else if (targetType == IslandCardType.Discord)
        {
            ShowDiscord(false);
        }
        else if (targetType == IslandCardType.Apps)
        {
            ShowApps(false);
        }
        else if (targetType == IslandCardType.Email)
        {
            ShowEmail(false);
        }
        else if (HasActiveMedia)
        {
            ShowMedia(false);
        }
    }

    public void OpenCurrentNotificationActivity()
    {
        var targetType = CurrentRotationItem ?? PrimaryCompanionItem;
        if (targetType == IslandCardType.WhatsApp) ShowWhatsApp(false);
        else if (targetType == IslandCardType.Discord) ShowDiscord(false);
        else if (targetType == IslandCardType.Apps) ShowApps(false);
        else if (targetType == IslandCardType.Email) ShowEmail(false);
        else if (HasActiveMedia) ShowMedia(false);
    }

    public void OpenEmail()
    {
        if (CurrentEmail != null)
        {
            var email = CurrentEmail;
            _mailService.OpenEmail(email);
            // Opening the email in Outlook/Mailspring handles it, so dismiss notification from the dock
            DismissEmail();
            IsExpanded = false;
        }
    }

    /// <summary>
    /// The summary card's Open Mail: opens the newest mail where it came from (Mailspring, the account's web inbox or Outlook),
    /// like the Email card's Open. Never mailto:, which every mail client treats as "write a new message".
    /// </summary>
    public void OpenSummaryEmail()
    {
        var newest = RecentEmailMessages.Count > 0 ? RecentEmailMessages[0] : null;
        if (newest != null) _mailService.OpenEmail(newest);
        else ShowEmail(false);
    }

    public void CopyEmailVerificationCode()
    {
        if (CurrentEmail != null && !string.IsNullOrWhiteSpace(CurrentEmail.ExtractedVerificationCode))
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(CurrentEmail.ExtractedVerificationCode);
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
        }
    }

    public void ReplyEmail()
    {
        if (CurrentEmail != null)
        {
            _mailService.OpenEmail(CurrentEmail);
            IsExpanded = false;
        }
    }

    public void MarkEmailAsRead()
    {
        if (CurrentEmail != null)
        {
            _mailService.MarkAsRead(CurrentEmail);
            DismissEmailMessage(CurrentEmail);
        }
    }

    public void SelectEmailMessage(EmailNotification? notif)
    {
        if (notif == null) return;
        CurrentEmail = notif;
    }

    public void DismissEmailMessage(EmailNotification? notif)
    {
        if (notif != null)
        {
            RecentEmailMessages.Remove(notif);
        }

        if (RecentEmailMessages.Count > 0)
        {
            CurrentEmail = RecentEmailMessages[0];
        }
        else
        {
            DismissEmail();
        }
    }

    public void ClearAllEmailMessages()
    {
        RecentEmailMessages.Clear();
        DismissEmail();
    }

    public void DismissEmail()
    {
        CurrentEmail = null;
        RecentEmailMessages.Clear();
        if (ActiveCardType == IslandCardType.Email)
        {
            if (HasActiveWhatsApp) ActiveCardType = IslandCardType.WhatsApp;
            else if (HasActiveDiscord) ActiveCardType = IslandCardType.Discord;
            else if (HasActiveApps) ActiveCardType = IslandCardType.Apps;
            else
            {
                ActiveCardType = ResolveNotificationDismissFallback();
                if (!_isPinned) IsExpanded = false;
            }

            MascotMood = _wasGaming ? MascotMood.Focused : (CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle);
        }
    }

    public void OpenDiscord()
    {
        _discordService.OpenDiscord();
        DismissDiscord();
        IsExpanded = false;
    }

    public void SelectDiscordMessage(DiscordNotification? notif)
    {
        if (notif == null) return;
        CurrentDiscord = notif;
    }

    public void DismissDiscordMessage(DiscordNotification? notif)
    {
        if (notif != null)
        {
            RecentDiscordMessages.Remove(notif);
        }

        if (RecentDiscordMessages.Count > 0)
        {
            CurrentDiscord = RecentDiscordMessages[0];
        }
        else
        {
            DismissDiscord();
        }
    }

    public void ClearAllDiscordMessages()
    {
        RecentDiscordMessages.Clear();
        DismissDiscord();
    }

    public void DismissDiscord()
    {
        CurrentDiscord = null;
        RecentDiscordMessages.Clear();
        if (ActiveCardType == IslandCardType.Discord)
        {
            if (HasActiveWhatsApp) ActiveCardType = IslandCardType.WhatsApp;
            else if (HasActiveEmail) ActiveCardType = IslandCardType.Email;
            else if (HasActiveApps) ActiveCardType = IslandCardType.Apps;
            else
            {
                ActiveCardType = ResolveNotificationDismissFallback();
                if (!_isPinned) IsExpanded = false;
            }

            MascotMood = _wasGaming ? MascotMood.Focused : (CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle);
        }
    }

    public async Task SendWhatsAppReplyAsync()
    {
        if (CurrentWhatsApp != null && !string.IsNullOrWhiteSpace(ReplyText))
        {
            string jid = CurrentWhatsApp.FromJid, device = CurrentWhatsApp.DeviceId;
            bool success = await _gowaService.SendReplyAsync(jid, ReplyText, device);

            if (success)
            {
                await MarkWhatsAppChatReadAsync(jid, device);
                ReplyText = "";
                DismissWhatsApp();
            }
        }
    }

    // After you answer a chat it counts as read: tell WhatsApp, so the phone and the other docks clear it too.
    private async Task MarkWhatsAppChatReadAsync(string jid, string device)
    {
        var ids = MochiDock.Core.WhatsAppReadIds.Pick(RecentWhatsAppMessages.Select(m => (m.RemoteId, m.FromJid, m.DeviceId)).ToList(), jid, device);
        if (ids.Count > 0) await _gowaService.MarkChatReadAsync(jid, ids, device);
    }

    public void SelectWhatsAppMessage(WhatsAppNotification? notif)
    {
        if (notif == null) return;
        CurrentWhatsApp = notif;
        ReplyText = "";
    }

    public void DismissWhatsAppMessage(WhatsAppNotification? notif)
    {
        if (notif != null)
        {
            RecentWhatsAppMessages.Remove(notif);

            // Also remove from conversation thread
            if (SelectedWhatsAppConversation != null)
            {
                SelectedWhatsAppConversation.Messages.Remove(notif);
                if (SelectedWhatsAppConversation.UnreadCount > 0)
                {
                    SelectedWhatsAppConversation.UnreadCount--;
                }
                if (SelectedWhatsAppConversation.Messages.Count == 0)
                {
                    ActiveWhatsAppConversations.Remove(SelectedWhatsAppConversation);
                    SelectedWhatsAppConversation = ActiveWhatsAppConversations.FirstOrDefault();
                }
            }
        }

        if (RecentWhatsAppMessages.Count > 0)
        {
            CurrentWhatsApp = RecentWhatsAppMessages[0];
        }
        else
        {
            DismissWhatsApp();
        }
    }

    public void ClearAllWhatsAppMessages()
    {
        RecentWhatsAppMessages.Clear();
        ActiveWhatsAppConversations.Clear();
        SelectedWhatsAppConversation = null;
        DismissWhatsApp();
    }

    public void DismissWhatsApp()
    {
        CurrentWhatsApp = null;
        RecentWhatsAppMessages.Clear();
        ActiveWhatsAppConversations.Clear();
        SelectedWhatsAppConversation = null;
        if (ActiveCardType == IslandCardType.WhatsApp)
        {
            if (HasActiveDiscord) ActiveCardType = IslandCardType.Discord;
            else if (HasActiveEmail) ActiveCardType = IslandCardType.Email;
            else if (HasActiveApps) ActiveCardType = IslandCardType.Apps;
            else
            {
                ActiveCardType = ResolveNotificationDismissFallback();
                if (!_isPinned) IsExpanded = false;
            }

            MascotMood = _wasGaming ? MascotMood.Focused : (CurrentMedia.IsPlaying ? MascotMood.Music : MascotMood.Idle);
        }
    }

    /// <summary>True when "only notify for listed groups" is on and the shown notification is a group that is not on the list yet.</summary>
    public bool CanAllowCurrentWhatsAppGroup =>
        _settings.WhatsAppOnlyListedGroups && _currentWhatsApp is { IsGroup: true } current && !string.IsNullOrWhiteSpace(current.FromJid) &&
        !MochiDock.Core.GowaAccountPolicy.IsMuted(_settings.WhatsAppAllowedGroups, current.FromJid, current.DeviceId);

    /// <summary>Puts the group of the shown notification on the notify list, so it keeps notifying while only listed groups do.</summary>
    public void AllowCurrentWhatsAppGroup()
    {
        if (_currentWhatsApp is not { IsGroup: true } current || string.IsNullOrWhiteSpace(current.FromJid)) return;
        string key = _gowaService.MuteKeyFor(current.FromJid, current.DeviceId);
        if (!_settings.WhatsAppAllowedGroups.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            _gowaService.AddAllowedGroup(key);
            UpdateSettings(x =>
            {
                x.WhatsAppAllowedGroups.Add(key);
                x.WhatsAppChatNames[current.FromJid] = !string.IsNullOrWhiteSpace(current.GroupName) ? current.GroupName : current.SenderName;
            });
        }
        OnPropertyChanged(nameof(CanAllowCurrentWhatsAppGroup));
    }

    public void MuteCurrentWhatsAppChat()
    {
        if (CurrentWhatsApp == null) return;
        string chatJid = CurrentWhatsApp.FromJid;
        string device = CurrentWhatsApp.DeviceId;
        if (string.IsNullOrWhiteSpace(chatJid)) return;

        // With several accounts the mute covers only the account that received this message.
        string muteKey = _gowaService.MuteKeyFor(chatJid, device);
        if (!_settings.MutedWhatsAppChatJids.Contains(muteKey, StringComparer.OrdinalIgnoreCase))
        {
            _gowaService.AddMutedChat(muteKey);
            UpdateSettings(x =>
            {
                x.MutedWhatsAppChatJids.Add(muteKey);
                x.WhatsAppChatNames[chatJid] = CurrentWhatsApp.IsGroup && !string.IsNullOrWhiteSpace(CurrentWhatsApp.GroupName) ? CurrentWhatsApp.GroupName : CurrentWhatsApp.SenderName;
            });
        }

        // Dismiss this chat immediately
        if (SelectedWhatsAppConversation != null && string.Equals(SelectedWhatsAppConversation.ChatJid, chatJid, StringComparison.OrdinalIgnoreCase) &&
            MochiDock.Core.GowaAccountPolicy.AppliesTo(SelectedWhatsAppConversation.DeviceId, device))
        {
            ActiveWhatsAppConversations.Remove(SelectedWhatsAppConversation);
            SelectedWhatsAppConversation = ActiveWhatsAppConversations.FirstOrDefault();
        }

        for (int i = RecentWhatsAppMessages.Count - 1; i >= 0; i--)
        {
            if (string.Equals(RecentWhatsAppMessages[i].FromJid, chatJid, StringComparison.OrdinalIgnoreCase) &&
                MochiDock.Core.GowaAccountPolicy.AppliesTo(RecentWhatsAppMessages[i].DeviceId, device))
            {
                RecentWhatsAppMessages.RemoveAt(i);
            }
        }

        if (RecentWhatsAppMessages.Count > 0)
        {
            CurrentWhatsApp = RecentWhatsAppMessages[0];
        }
        else
        {
            DismissWhatsApp();
        }
    }

    public void OpenWhatsAppDashboard()
    {
        _gowaService.OpenDashboard();
    }

    public void LaunchWhatsAppApp()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("whatsapp:") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://web.whatsapp.com") { UseShellExecute = true });
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
        }
    }
}
