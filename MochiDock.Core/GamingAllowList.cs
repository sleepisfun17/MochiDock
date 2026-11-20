using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>One person or chat whose notifications are shown even while a game is running.</summary>
public sealed class GamingAllowEntry
{
    /// <summary>"WhatsApp" or "Discord".</summary>
    public string Kind { get; set; } = "";
    /// <summary>WhatsApp: the chat id (or "device|chat" with several accounts, as in the muted list). Discord: the sender's name.</summary>
    public string Key { get; set; } = "";
    /// <summary>What to show in the settings list: the contact, group or sender name.</summary>
    public string Label { get; set; } = "";
}

/// <summary>Pure rules for the "let through while gaming" list.</summary>
public static class GamingAllowList
{
    public const string WhatsApp = "WhatsApp", Discord = "Discord";

    /// <summary>True when a WhatsApp message from <paramref name="chatJid"/> (received by <paramref name="deviceId"/>) may interrupt a game.</summary>
    public static bool AllowsWhatsApp(IEnumerable<GamingAllowEntry>? entries, string? chatJid, string? deviceId) =>
        !string.IsNullOrWhiteSpace(chatJid) && entries != null &&
        GowaAccountPolicy.IsMuted(Keys(entries, WhatsApp), chatJid, deviceId);   // the same chat and account matching as the muted list

    /// <summary>True when a Discord message from <paramref name="sender"/> may interrupt a game (name equal after trimming, ignoring case).</summary>
    public static bool AllowsDiscord(IEnumerable<GamingAllowEntry>? entries, string? sender) =>
        !string.IsNullOrWhiteSpace(sender) && entries != null &&
        Keys(entries, Discord).Any(k => string.Equals(k.Trim(), sender.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Keys(IEnumerable<GamingAllowEntry> entries, string kind) =>
        entries.Where(e => string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.Key)).Select(e => e.Key);

    /// <summary>Adds an entry unless the same kind and key (ignoring case) is already there; returns the new list.</summary>
    public static List<GamingAllowEntry> Add(IEnumerable<GamingAllowEntry> entries, GamingAllowEntry entry)
    {
        var list = entries.ToList();
        bool exists = list.Any(e => string.Equals(e.Kind, entry.Kind, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(e.Key.Trim(), entry.Key.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!exists && !string.IsNullOrWhiteSpace(entry.Key)) list.Add(entry);
        return list;
    }

    /// <summary>The short text for the gaming capsule: "Sender: message" on one line, trimmed to <paramref name="max"/> characters.</summary>
    public static string CapsuleText(string? sender, string? message, int max = 70)
    {
        string text = $"{(sender ?? "").Trim()}: {(message ?? "").Replace("\r", " ").Replace("\n", " ").Trim()}".Trim(' ', ':');
        return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
    }
}
