using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace MochiDock.Core;

/// <summary>One chat from the server's chat list, as the settings picker shows it.</summary>
public sealed record WhatsAppChatEntry(string Jid, string DeviceId, string AccountName, string Name, bool IsGroup, DateTime? LastMessageUtc, bool Archived);

/// <summary>Which chats the picker lists.</summary>
public enum WhatsAppChatKind { All, Groups, People }

/// <summary>
/// Reads the GOWA chat list (GET /chats) for the settings picker. WhatsApp's own mute state is not part of that list, so the picker lets
/// the user say which chats to silence (or, with "only listed", which to hear from). Pure: no network, no settings.
/// </summary>
public static class WhatsAppChatList
{
    /// <summary>Chats of one account. Status updates, broadcast lists and channels are skipped; odd or missing fields never throw.</summary>
    public static IReadOnlyList<WhatsAppChatEntry> Parse(JsonElement root, string deviceId, string accountName)
    {
        var list = new List<WhatsAppChatEntry>();
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Object ||
            !results.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;

        foreach (var chat in data.EnumerateArray())
        {
            if (chat.ValueKind != JsonValueKind.Object) continue;
            string jid = Text(chat, "jid").Trim();
            if (jid.Length == 0 || jid.Contains("@broadcast", StringComparison.OrdinalIgnoreCase) || jid.Contains("@newsletter", StringComparison.OrdinalIgnoreCase)) continue;
            bool isGroup = jid.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase);
            string name = Text(chat, "name").Trim();
            DateTime? last = DateTime.TryParse(Text(chat, "last_message_time"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
            bool archived = chat.TryGetProperty("archived", out var a) && a.ValueKind == JsonValueKind.True;
            list.Add(new WhatsAppChatEntry(jid, deviceId, accountName, name, isGroup, last, archived));
        }
        return list;
    }

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>What to call the chat: its name, else the phone number, else a plain word.</summary>
    public static string DisplayName(WhatsAppChatEntry chat)
    {
        if (!string.IsNullOrWhiteSpace(chat.Name) && !chat.Name.StartsWith("Group ", StringComparison.OrdinalIgnoreCase)) return chat.Name.Trim();
        if (chat.IsGroup) return "Group";
        // Only a phone id carries a phone number; a linked id (@lid) is an opaque number, not one to show.
        bool phone = chat.Jid.EndsWith("@s.whatsapp.net", StringComparison.OrdinalIgnoreCase) || chat.Jid.EndsWith("@c.us", StringComparison.OrdinalIgnoreCase);
        string number = phone ? MentionResolver.NumberOf(chat.Jid) : "";
        return number.Length > 0 ? "+" + number : "Unnamed contact";
    }

    /// <summary>Newest first (chats with no time last), filtered by kind and by a search text matched against the name and the id.</summary>
    public static IReadOnlyList<WhatsAppChatEntry> Filter(IEnumerable<WhatsAppChatEntry> chats, string? query, WhatsAppChatKind kind)
    {
        string q = (query ?? "").Trim();
        return chats
            .Where(c => kind == WhatsAppChatKind.All || (kind == WhatsAppChatKind.Groups) == c.IsGroup)
            .Where(c => q.Length == 0 || DisplayName(c).Contains(q, StringComparison.OrdinalIgnoreCase) || c.Jid.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.LastMessageUtc ?? DateTime.MinValue)
            .ThenBy(c => DisplayName(c), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The name to show for a list entry such as "628123@s.whatsapp.net" or "device|120363@g.us": the saved name, else the entry itself.</summary>
    public static string EntryLabel(string entry, IReadOnlyDictionary<string, string>? names)
    {
        string jid = entry;
        int bar = jid.IndexOf('|');
        if (bar >= 0) jid = jid[(bar + 1)..];
        if (names != null && names.TryGetValue(jid, out var name) && !string.IsNullOrWhiteSpace(name)) return name;
        return entry;
    }
}
