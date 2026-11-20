namespace MochiDock.Models;

public class AppSettings
{
    public bool ShowFxRatesTab { get; set; } = false;
    public bool ShowTodayTab { get; set; }
    public bool EnableDeviceBatteries { get; set; }
    public bool ShowDeviceBatteryBackground { get; set; }
    /// <summary>Logs total power in the background (session energy and history). On by default; turning it off keeps the history.</summary>
    public bool EnablePowerLogging { get; set; } = true;
    /// <summary>Interface size in percent (80 to 200): an app-only zoom on top of Windows display scaling. Read it through <c>InterfaceScale.Clamp</c>.</summary>
    public int InterfaceScalePercent { get; set; } = 100;
    /// <summary>The PC's other power consumers (board, memory, drives, fans...) with estimated watts; their sum replaces the fixed allowance. Empty until detected.</summary>
    public List<MochiDock.Core.PowerComponent> PowerComponents { get; set; } = new();
    public List<string> HiddenDeviceBatteryKeys { get; set; } = new();
    public bool ShowDeviceBatteriesOnTaskbar { get; set; } = true;
    public bool ShowAiUsageTab { get; set; } = false;
    public bool ShowScriptTab { get; set; } = false;
    public bool ShowDownloadsTab { get; set; } = false;
    public bool HideInFullscreen { get; set; }
    public bool EnableHardwareAlerts { get; set; } = true;
    // Silent capsule notices when the charger is connected / removed and when the battery runs low (laptops only).
    public bool EnablePowerAlerts { get; set; } = true;
    public double? IslandHorizontalRatio { get; set; } = null;
    public bool ExcludeFromScreenCapture { get; set; } = false;

    // AI Usage Preferences
    public bool EnableAiUsageAlerts { get; set; } = false;
    public List<string> HiddenAiProviders { get; set; } = new();

    // Script / Teleprompter Preferences
    public int ScriptWpm { get; set; } = 140;
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
    // Display & Window
    public string TargetMonitor { get; set; } = "";
    public double TopMargin { get; set; } = 4;
    public const double DefaultGamingTopOffset = 150;
    public double GamingTopOffset { get; set; } = DefaultGamingTopOffset;
    /// <summary>Configured gaming offset, or the default when unset or non-positive.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double EffectiveGamingTopOffset => GamingTopOffset > 0 ? GamingTopOffset : DefaultGamingTopOffset;
    public string DockStyle { get; set; } = "Floating"; // "Floating" or "Notch"

    // Telemetry & Pin States
    public bool IsStatsPinnedToCapsule { get; set; } = false;
    public bool IsHardwareGamingMode { get; set; } = true;
    public bool GameOverlayReadoutPeeks { get; set; } = false; // Inactive legacy preference retained for settings round-trip compatibility.
    public string ActiveSystemModule { get; set; } = "CPU"; // "CPU", "GPU", "Memory", "Drives", "Network", "Games"
    public bool IsExpandedPinned { get; set; } = false;
    public string LastActiveCardType { get; set; } = "Media";
    public string FxHistorySpan { get; set; } = "7D"; // "7D", "1M", "1Y"

    // Display Resolution & Gaming Click-Through
    /// <summary>Gaming resolution for the hotkey and tray, e.g. "1728x1080". Empty (new installs) = the display's own 16:10 suggestion, see GamingResolutionOptions.Resolve.</summary>
    public string PreferredGamingResolution { get; set; } = "";
    public bool EnableGamingClickThrough { get; set; } = true;
    public bool EnableResolutionHotkey { get; set; } = true;
    /// <summary>Record FPS statistics for each gaming session and show a summary when it ends.</summary>
    public bool EnableGameSessionSummary { get; set; } = true;
    /// <summary>Last size of the Advanced Settings window in DIPs; 0 means use the default size.</summary>
    /// <summary>Host pinged during games to measure network quality (the route to it, not the game server).</summary>
    public string PingHost { get; set; } = "1.1.1.1";
    /// <summary>Show the ping next to the FPS in the pinned gaming capsule.</summary>
    public bool ShowPingInGameCapsule { get; set; }
    /// <summary>Remind the player to take a break every this many minutes of one gaming session; 0 = off, otherwise 60, 90 or 120.</summary>
    public int BreakReminderMinutes { get; set; }
    /// <summary>While League of Legends is played, read the match result and kills/deaths/assists from the game's official local API.</summary>
    public bool EnableLolMatchStats { get; set; } = true;
    /// <summary>After a PUBG session, look the match up in the official PUBG API (needs a player name and an API key saved in Windows Credential Manager).</summary>
    public bool EnablePubgStats { get; set; } = true;
    /// <summary>The in-game player name (the PUBG API is case-sensitive).</summary>
    public string PubgPlayerName { get; set; } = "";
    /// <summary>steam, kakao, xbox or psn.</summary>
    public string PubgPlatform { get; set; } = "steam";
    /// <summary>Per-game changes (resolution, click-through, notifications) applied while that game is played.</summary>
    public List<MochiDock.Core.GameProfile> GameProfiles { get; set; } = new();
    public double SettingsWindowWidth { get; set; }
    public double SettingsWindowHeight { get; set; }
    /// <summary>Global shortcut that toggles the display resolution, for example "Ctrl+Alt+R".</summary>
    public string ResolutionHotkey { get; set; } = "Ctrl+Alt+R";
    /// <summary>Global shortcuts that resize the active window to a preset size and centre it (replaces the AutoHotkey WindowResizer script).</summary>
    public bool EnableWindowSizeShortcuts { get; set; } = false;
    public List<MochiDock.Core.WindowSizePreset> WindowSizePresets { get; set; } = MochiDock.Core.WindowSizing.Defaults();
    /// <summary>Shortcut that swaps the active window's width and height.</summary>
    public bool EnableWindowFlip { get; set; } = true;
    public string WindowFlipHotkey { get; set; } = MochiDock.Core.WindowSizing.DefaultFlipHotkey;

    // Mascot & Smart Modes
    /// <summary>People and chats whose notifications are shown during games even though "Do not disturb while gaming" is on.</summary>
    public List<MochiDock.Core.GamingAllowEntry> GamingAllowList { get; set; } = new();
    public bool EnableGamingDnd { get; set; } = true;
    public bool EnableOvernightGreeting { get; set; } = true;
    /// <summary>Remind before a calendar event or a task with a due time starts.</summary>
    public bool EnableAgendaReminders { get; set; } = true;
    /// <summary>Minutes before the start; one of 0, 5, 10, 15, 30.</summary>
    public int AgendaReminderLeadMinutes { get; set; } = 10;
    /// <summary>What a reminder does while a fullscreen app hides the dock: "Off", "Sound" or "SoundAndNotification".</summary>
    public string HiddenReminderFallback { get; set; } = "SoundAndNotification";
    /// <summary>Remind about an open task again when it is due and 10 and 30 minutes after, until you acknowledge it or tick it off.</summary>
    public bool RemindOverdueTasks { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;

    // Messaging Integrations
    /// <summary>WhatsApp (GOWA) is opt-in: off means no GOWA process, event listener or polling. See <see cref="MigrateWhatsAppOptIn"/>.</summary>
    /// <summary>Ask GitHub for a newer release at startup and every 12 hours. Read-only; the user installs updates themselves.</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>When the last update check finished (UTC), for display only.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }
    public bool EnableWhatsApp { get; set; }
    /// <summary>Set once the opt-in switch was introduced; fresh installs start with it set (and WhatsApp off).</summary>
    public bool WhatsAppOptInMigrated { get; set; }
    public string GowaMode { get; set; } = "Local"; // "Local" or "Remote"
    public string RemoteGowaUrl { get; set; } = "";
    /// <summary>Secret the shared GOWA server must present when it forwards read receipts to this PC (generated automatically).</summary>
    public string RemoteWebhookToken { get; set; } = "";
    public int GowaPort { get; set; } = 3099;
    public int WebhookPort { get; set; } = 47823;
    public bool FilterAds { get; set; } = true;
    /// <summary>Watch only the Outlook folders listed in OutlookTrackedFolders (with their subfolders), not every Inbox.</summary>
    public bool OutlookOnlyListedFolders { get; set; }
    /// <summary>Outlook folders (besides every Inbox) to watch together with all their subfolders. Edit in settings.json.</summary>
    public List<string> OutlookTrackedFolders { get; set; } = new();
    /// <summary>IMAP mailboxes (Gmail, Yahoo, iCloud...) watched for new mail. Passwords are not stored here; see <c>MailCredentialStore</c>.</summary>
    public List<MochiDock.Core.ImapAccount> ImapAccounts { get; set; } = new();
    public bool IgnoreArchivedChats { get; set; } = true;
    public bool MuteAllWhatsAppGroups { get; set; } = false;
    public List<string> MutedWhatsAppChatJids { get; set; } = new();
    /// <summary>WhatsApp does not tell MochiDock which groups were muted in the app, so this switch inverts the rule: only the groups in <see cref="WhatsAppAllowedGroups"/> notify.</summary>
    public bool WhatsAppOnlyListedGroups { get; set; }
    /// <summary>Group chat ids (or "device|chat" with several accounts) that notify while <see cref="WhatsAppOnlyListedGroups"/> is on.</summary>
    public List<string> WhatsAppAllowedGroups { get; set; } = new();
    /// <summary>Same inversion for direct chats: only the people on the notify list (<see cref="WhatsAppAllowedGroups"/>) notify.</summary>
    public bool WhatsAppOnlyListedPeople { get; set; }
    /// <summary>Chat id to display name, saved by the chat picker so the muted and notify lists show names instead of ids.</summary>
    public Dictionary<string, string> WhatsAppChatNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Notification controls
    public bool MuteEmail { get; set; }
    public bool MuteDiscord { get; set; }
    /// <summary>Mutes the Apps card (notifications from <see cref="WatchedApps"/>).</summary>
    public bool MuteApps { get; set; }
    /// <summary>Apps whose Windows notifications are shown on the Apps card. Empty (the default) turns the card off.</summary>
    public List<MochiDock.Core.WatchedApp> WatchedApps { get; set; } = new();
    public bool MuteWhatsApp { get; set; }
    public bool AutoExpandNotificationsOnArrival { get; set; } = false; // Minimal capsule notice by default (expand on hover)
    public bool QuietHoursEnabled { get; set; }
    public string QuietHoursStart { get; set; } = "22:00";
    public string QuietHoursEnd { get; set; } = "07:00";
    public long SnoozeUntilTicks { get; set; }

    // Capsule customization and hardware alerts
    private string capsuleTheme = "Default";
    public string CapsuleTheme { get => capsuleTheme; set => capsuleTheme = MochiDock.Core.ThemePolicy.Normalize(value); }
    public double CapsuleScrollSpeed { get; set; } = 35;
    public double CpuAlertThreshold { get; set; } = 90;
    public double GpuAlertThreshold { get; set; } = 90;
    public double TemperatureAlertThreshold { get; set; } = 85;

    // Weather
    public string WeatherCity { get; set; } = "Tokyo";
    /// <summary>Mochi shows more faces (angry, hot, freezing, dizzy, drained) for overdue items, temperature, load and battery.</summary>
    public bool ExpressiveMochi { get; set; } = true;
    /// <summary>The user's birthday as "MM-dd" (empty: none). Mochi wears a party hat all day.</summary>
    public string BirthdayMonthDay { get; set; } = "";
    public bool ShowWeatherInCapsule { get; set; } = true;
    /// <summary>A short capsule notice when rain, snow or a thunderstorm is expected within the hour.</summary>
    public bool RainSoonNotice { get; set; } = true;
    /// <summary>Resting capsule with weather: "EqualEdges" (clock exactly centred, Mochi and the weather at the edges) or "EvenSpacing" (the same space before, between and after the three items). "EqualGaps" from an earlier build reads as EvenSpacing.</summary>
    public string CapsuleClockLayout { get; set; } = "EqualEdges";
    /// <summary>How dates are shown: "Automatic" (Windows region), "YearFirst" (2026/11/19 木), "DayFirst", "MonthFirst" or "Iso". See <see cref="MochiDock.Core.DateDisplay"/>.</summary>
    public string DateFormat { get; set; } = "Automatic";

    // Feature Tabs Visibility (Header Bar)
    public bool ShowStatsTab { get; set; } = true;
    public bool ShowWeatherTab { get; set; } = true;
    public bool ShowPomodoroTab { get; set; } = false;
    /// <summary>Set once the former Tasks tab was merged into Today (see <see cref="MigrateTasksTab"/>).</summary>
    public bool TasksTabMerged { get; set; }
    public bool ShowFileShelfTab { get; set; } = false;
    public bool ShowClipboardTab { get; set; } = false;

    // Lyrics & Pronunciation
    public bool ShowLyricsPronunciation { get; set; } = true;
    public bool ShowLyricsFurigana { get; set; } = true;
    // Lyric timing shift in milliseconds; positive = lines appear earlier. Clamped to +/-5000 on load.
    public int LyricsOffsetMs { get; set; } = 0;
    // Fetch and show synced lyrics; turning it off also stops lyric lookups.
    public bool EnableSyncedLyrics { get; set; } = true;
    // Short UI sounds (notification pop, mascot). Prayer audio has its own Tone / Silent setting.
    public bool EnableSounds { get; set; } = true;

    // Prayer Times
    public bool EnablePrayerTimes { get; set; } = false;
    public string PrayerCalculationMethod { get; set; } = "Kemenag"; // "Kemenag", "MWL", "ISNA", "Egypt", "UmmAlQura"
    public bool EnableAdhanAudio { get; set; } = false;
    public string PrayerAudioWavPath { get; set; } = "";
    public int PrayerAudioVersion { get; set; }
    public string PrayerSoundMode { get; set; } = "Tone"; // "Adzan", "Tone", "Silent"
    public bool EnablePrayerPreNotification { get; set; } = true;
    public bool ShowPrayerInCapsule { get; set; } = true;

    // Calendar Holidays
    public bool ShowJapanHolidays { get; set; } = false;
    public bool ShowIndonesiaHolidays { get; set; } = false;
    public bool WeekStartsOnMonday { get; set; } = false;
    public bool DownloadAudioFormat { get; set; } = false;

    /// <summary>
    /// One-time migration: the Tasks tab is now part of Today. Someone who had the Tasks tab on (it was on by default) keeps a tab
    /// with their tasks by turning Today on; someone who had hidden it is left alone. The old key is dropped from the unknown-field bag.
    /// </summary>
    public void MigrateWhatsAppOptIn(bool hadGowaSession)
    {
        if (WhatsAppOptInMigrated) return;
        WhatsAppOptInMigrated = true;
        if (hadGowaSession || string.Equals(GowaMode, "Remote", StringComparison.OrdinalIgnoreCase)) EnableWhatsApp = true;
    }

    /// <summary>
    /// One-time migration: WhatsApp became opt-in. Someone who already linked a device (local session data) or uses a remote server
    /// keeps it on; everyone else starts with it off.
    /// </summary>
    public void MigrateTasksTab()
    {
        if (TasksTabMerged) return;
        TasksTabMerged = true;
        bool hadTasksTab = true;
        if (ExtensionData != null && ExtensionData.TryGetValue("ShowTasksTab", out var old))
        {
            hadTasksTab = old.ValueKind != System.Text.Json.JsonValueKind.False;
            ExtensionData.Remove("ShowTasksTab");
        }
        if (hadTasksTab) ShowTodayTab = true;
    }
}
