using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>One entry of the Advanced Settings sidebar.</summary>
public sealed record SettingsSection(string Id, string Title, string Description, string Group);

/// <summary>Sidebar sections of the Advanced Settings window: ids, titles, order and deep-link targets.</summary>
public static class SettingsSections
{
    public const string General = "general";
    public const string Appearance = "appearance";
    public const string Notifications = "notifications";
    public const string WhatsApp = "whatsapp";
    public const string Email = "email";
    public const string Location = "location";
    public const string Calendar = "calendar";
    public const string Tabs = "tabs";
    public const string Gaming = "gaming";
    public const string Hardware = "hardware";
    public const string Music = "music";
    public const string AiUsage = "ai";
    public const string Data = "data";

    /// <summary>Sidebar group headings, in order. Sections of one group are listed together in <see cref="All"/>.</summary>
    public const string GroupDock = "Dock", GroupIntegrations = "Integrations", GroupFeatures = "Features", GroupData = "Data";

    /// <summary>Sidebar order, top to bottom.</summary>
    public static IReadOnlyList<SettingsSection> All { get; } = new SettingsSection[]
    {
        new(General, "General", "Startup, fullscreen behaviour and screen capture.", GroupDock),
        new(Appearance, "Appearance & Dock", "Theme, dock style, monitor and capsule content.", GroupDock),
        new(Tabs, "Tabs", "Which tabs and modules the dock shows.", GroupDock),
        new(WhatsApp, "WhatsApp", "GOWA server, login, read sync and muted chats.", GroupIntegrations),
        new(Email, "Email", "Outlook folders to watch and ad filtering.", GroupIntegrations),
        new(Calendar, "Calendar", "Calendar link, week start and holidays.", GroupIntegrations),
        new(Location, "Location & Prayer", "City, weather and prayer times.", GroupIntegrations),
        new(Notifications, "Notifications", "Mutes, quiet hours, snooze, reminders and sounds.", GroupFeatures),
        new(Gaming, "Gaming & Display", "Resolution presets, window-size shortcuts, click-through, gaming offset and session summary.", GroupFeatures),
        new(Hardware, "Hardware & power", "Alerts, wireless device batteries and power estimate.", GroupFeatures),
        new(Music, "Music & lyrics", "Synced lyrics, pronunciation and timing.", GroupFeatures),
        new(AiUsage, "AI usage", "Which AI providers are tracked and alerts.", GroupFeatures),
        new(Data, "Data & diagnostics", "Export, import, reload, files, logs and integration status.", GroupData),
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
}
