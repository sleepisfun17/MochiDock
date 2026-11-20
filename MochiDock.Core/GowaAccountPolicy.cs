using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MochiDock.Core;

/// <summary>One WhatsApp account (a GOWA "device") on the server.</summary>
public sealed record GowaAccount(string DeviceId, string Jid, string Name);

/// <summary>Pure rules for telling WhatsApp accounts apart: matching ids and deciding when to show an account label.</summary>
public static class GowaAccountPolicy
{
    /// <summary>
    /// True when both ids name the same account. Ids match exactly (ignoring case) or by phone number,
    /// because one server call may report "628123@s.whatsapp.net" and another "628123:12@s.whatsapp.net".
    /// Empty ids never match.
    /// </summary>
    public static bool SameAccount(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        string na = MentionResolver.NumberOf(a), nb = MentionResolver.NumberOf(b);
        return na.Length > 0 && na == nb;
    }

    /// <summary>True when the account of a message may be affected by an event for <paramref name="eventDevice"/>; an unknown side matches everything.</summary>
    public static bool AppliesTo(string? messageDevice, string? eventDevice) =>
        string.IsNullOrWhiteSpace(messageDevice) || string.IsNullOrWhiteSpace(eventDevice) || SameAccount(messageDevice, eventDevice);

    /// <summary>The account labels appear only when more than one account is linked.</summary>
    public static bool ShowLabels(int accountCount) => accountCount > 1;

    /// <summary>
    /// The muted-list entry for a chat. With several accounts the mute applies to the account that received the message
    /// ("device|chat"); with one account, or an unknown account, it is the plain chat id and applies everywhere.
    /// </summary>
    public static string MuteKey(string? deviceId, string chatJid, int accountCount) =>
        accountCount > 1 && !string.IsNullOrWhiteSpace(deviceId) ? deviceId.Trim() + "|" + chatJid : chatJid;

    /// <summary>
    /// True when a group message must stay silent because "only notify for listed groups" is on and the group is not on the list.
    /// Entries use the same format as the muted list, so with several accounts a listing can be limited to one account.
    /// </summary>
    public static bool IsHiddenByGroupAllowList(bool onlyListedGroups, IEnumerable<string> allowed, string chatJid, string? deviceId, bool isGroup) =>
        IsHiddenByAllowList(onlyListedGroups, false, allowed, chatJid, deviceId, isGroup);

    /// <summary>
    /// True when a message must stay silent because "only notify for listed groups" (for a group) or "only notify for listed people"
    /// (for a direct chat) is on and the chat is not on the notify list. WhatsApp does not tell MochiDock which chats were muted in the app.
    /// </summary>
    public static bool IsHiddenByAllowList(bool onlyListedGroups, bool onlyListedPeople, IEnumerable<string> allowed, string chatJid, string? deviceId, bool isGroup) =>
        (isGroup ? onlyListedGroups : onlyListedPeople) && !IsMuted(allowed, chatJid, deviceId);

    /// <summary>True when a muted-list entry covers <paramref name="chatJid"/> received by <paramref name="deviceId"/>. Plain entries cover every account.</summary>
    public static bool IsMuted(IEnumerable<string> entries, string chatJid, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(chatJid)) return false;
        string clean = chatJid.Replace("@s.whatsapp.net", "").Replace("@c.us", "").Replace("@g.us", "").Replace("@lid", "").Trim();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            string chat = entry.Trim();
            int bar = chat.IndexOf('|');
            if (bar >= 0)
            {
                if (!AppliesTo(deviceId, chat[..bar])) continue;   // an unknown device on either side matches
                chat = chat[(bar + 1)..];
            }
            if (string.Equals(chat, chatJid, StringComparison.OrdinalIgnoreCase) || string.Equals(chat, clean, StringComparison.OrdinalIgnoreCase)) return true;
            if (SamePhone(chat, chatJid)) return true;
        }
        return false;
    }

    // One person's phone id comes in forms such as "628123@s.whatsapp.net", "628123:12@s.whatsapp.net" and "628123@c.us": same number, same chat.
    // Groups, linked ids (@lid) and channels are different chats to WhatsApp and are never matched by their digits.
    private static bool SamePhone(string a, string b)
    {
        static bool Phone(string jid) => !jid.Contains("@g.us", StringComparison.OrdinalIgnoreCase) && !jid.Contains("@lid", StringComparison.OrdinalIgnoreCase) &&
            !jid.Contains("@newsletter", StringComparison.OrdinalIgnoreCase) && !jid.Contains("@broadcast", StringComparison.OrdinalIgnoreCase);
        if (!Phone(a) || !Phone(b)) return false;
        string na = MentionResolver.NumberOf(a);
        return na.Length > 0 && na == MentionResolver.NumberOf(b);
    }

    private static string Text(JsonElement entry, params string[] names)
    {
        foreach (var name in names)
            if (entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString()!;
        return "";
    }

    /// <summary>
    /// Accounts from a GOWA device list. Understands the device manager list (GET /devices: id, display_name, jid, state)
    /// and the older single-session list (GET /app/devices: device, name, jid). Accounts that are logged out are skipped,
    /// and entries with the same phone number count once.
    /// </summary>
    public static IReadOnlyList<GowaAccount> ParseAccounts(JsonElement results)
    {
        var accounts = new List<GowaAccount>();
        if (results.ValueKind != JsonValueKind.Array) return accounts;
        foreach (var entry in results.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            string state = Text(entry, "state", "status").ToLowerInvariant();
            if (state is "logged_out" or "not_logged_in" or "created" or "new" or "pending") continue;
            accounts.Add(new GowaAccount(Text(entry, "id", "device_id", "device"), Text(entry, "jid", "phone_number"), Text(entry, "display_name", "name", "push_name")));
        }
        return accounts
            .GroupBy(a => MentionResolver.NumberOf(a.Jid) is { Length: > 0 } number ? number : a.DeviceId + "|" + a.Jid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>The account whose id matches <paramref name="deviceId"/>, or null.</summary>
    public static GowaAccount? Find(System.Collections.Generic.IEnumerable<GowaAccount> accounts, string? deviceId)
    {
        foreach (var account in accounts)
            if (SameAccount(account.DeviceId, deviceId) || SameAccount(account.Jid, deviceId)) return account;
        return null;
    }
}
