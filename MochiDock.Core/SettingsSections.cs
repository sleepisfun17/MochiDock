using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>One entry of the Advanced Settings sidebar.</summary>
public sealed record SettingsSection(string Id, string Title, string Description, string Group, string Keywords = "");

/// <summary>Sidebar sections of the Advanced Settings window: ids, titles, order and deep-link targets.</summary>
public static class SettingsSections
{
    public const string General = "general";
    public const string Appearance = "appearance";
    public const string Mascot = "mascot";
    public const string Tabs = "tabs";
    public const string Taskbar = "taskbar";
    public const string Notifications = "notifications";
    public const string WhatsApp = "whatsapp";
    public const string Email = "email";
    public const string Location = "location";
    public const string Calendar = "calendar";
    public const string Music = "music";
    public const string Gaming = "gaming";
    public const string Hardware = "hardware";
    public const string AiUsage = "ai";
    public const string Data = "data";

    /// <summary>Sidebar group headings, in order. Sections of one group are listed together in <see cref="All"/>.</summary>
    public const string GroupDock = "Dock & Look",
        GroupMessaging = "Messaging & Alerts",
        GroupDaily = "Daily & Media",
        GroupPerformance = "PC & Gaming",
        GroupSystem = "System";

    /// <summary>Sidebar order, top to bottom.</summary>
    public static IReadOnlyList<SettingsSection> All { get; } = new SettingsSection[]
    {
        new(General, "General", "Startup, fullscreen behaviour, screen capture and date format.", GroupDock,
            "startup windows boot fullscreen hide screenshot screen capture date format regional"),
        new(Appearance, "Appearance & Dock", "Theme, dock style, interface size, monitor and capsule layout.", GroupDock,
            "theme dark matcha hojicha kuro night dock style floating notch scale size monitor display top margin reset position marquee speed weather clock spacing"),
        new(Mascot, "Mascot", "Character, status badge style, live preview, expressions and birthday.", GroupDock,
            "mochi neko kuro shiro character badge charm speech bubble sticker off preview expressive birthday party hat mouth whiskers"),
        new(Tabs, "Tabs & Modules", "Which tabs the dock shows and FX currency pairs.", GroupDock,
            "tabs modules media hardware weather pomodoro prayer file shelf clipboard today ai script downloads fx rates currency"),
        new(Taskbar, "Taskbar", "CPU, GPU, memory, disk, network, prayer, weather and device batteries beside the tray.", GroupDock,
            "taskbar tray readouts font color colour offset position arrows speed"),
        new(Notifications, "Notifications", "Quiet hours, snooze, reminders, other apps, sounds and mutes.", GroupMessaging,
            "quiet hours snooze gaming dnd popups reminders overdue other apps line telegram slack sounds mute"),
        new(WhatsApp, "WhatsApp", "GOWA server, login, read sync and muted chats.", GroupMessaging,
            "gowa qr phone link engine port webhook muted chats groups"),
        new(Email, "Email", "IMAP webmail accounts, Outlook folders and ad filtering.", GroupMessaging,
            "imap gmail yahoo icloud outlook mailspring folders ads filter password"),
        new(Location, "Location & Prayer", "City, weather, rain alerts and prayer times.", GroupDaily,
            "city weather rain forecast prayer adhan sound calculation method"),
        new(Calendar, "Calendar", "Calendar link, week start and holidays.", GroupDaily,
            "google calendar ical monday sunday japan indonesia holidays"),
        new(Music, "Music & Lyrics", "Synced lyrics, pronunciation, furigana and timing.", GroupDaily,
            "lyrics spotify lrclib romaji furigana japanese korean chinese offset timing"),
        new(Gaming, "Gaming & Display", "Resolution presets, window-size shortcuts, click-through, gaming offset and session stats.", GroupPerformance,
            "resolution hotkey window size shortcut flip click-through offset session summary break reminder lol league pubg ping"),
        new(Hardware, "Hardware & Power", "Alerts, 24-hour history, CPU sensors, wireless batteries and power logging.", GroupPerformance,
            "cpu gpu temperature threshold history pawnio sensors mouse headset wireless battery power log warm hot orange red colour color"),
        new(AiUsage, "AI Usage", "Which AI providers are tracked and usage alerts.", GroupPerformance,
            "claude codex gemini cursor provider alerts"),
        new(Data, "Updates & Data", "Updates, export, import, reload, files, logs and integration status.", GroupSystem,
            "updates version download export import reload settings file data folder diagnostic logs integrations"),
    };

    /// <summary>Deep-link names other parts of the app may use, mapped to a section id.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["prayer-place"] = Location,
        ["weather-city"] = Location,
        ["calendar-connect"] = Calendar,
        ["gowa"] = WhatsApp,
        ["outlook"] = Email,
        ["logs"] = Data,
        ["import-export"] = Data,
        ["taskbar-readouts"] = Taskbar,
        ["updates"] = Data,
        ["badge"] = Mascot,
        ["mochi"] = Mascot,
        ["fx"] = Tabs,
    };

    public static SettingsSection? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(s => string.Equals(s.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Maps a section id or deep-link alias to a known section id; anything else falls back to the first section.</summary>
    public static string Resolve(string? target)
    {
        var direct = Find(target);
        if (direct != null) return direct.Id;
        if (!string.IsNullOrWhiteSpace(target) && Aliases.TryGetValue(target.Trim(), out var id)) return id;
        return All[0].Id;
    }

    /// <summary>Returns true when every whitespace-separated token in <paramref name="query"/> appears in the section's text or keywords.</summary>
    public static bool Matches(SettingsSection section, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string haystack = $"{section.Title} {section.Description} {section.Group} {section.Keywords}";
        return tokens.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}
