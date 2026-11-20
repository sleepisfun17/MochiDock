using System;
using System.Collections.Generic;

namespace MochiDock.Core;

/// <summary>Picks which messages of a chat to mark as read on the server after the user answers it.</summary>
public static class WhatsAppReadIds
{
    /// <summary>The most messages marked per answer: a long backlog must not turn one reply into dozens of requests.</summary>
    public const int Cap = 20;

    /// <summary>
    /// The server ids (no blanks, no duplicates, newest first as given, at most <paramref name="cap"/>) of the messages that came from
    /// <paramref name="chatJid"/> on the account <paramref name="deviceId"/>. A message of another chat, or of another account, is never included.
    /// </summary>
    public static List<string> Pick(IEnumerable<(string RemoteId, string FromJid, string DeviceId)> messages, string chatJid, string deviceId, int cap = Cap)
    {
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(chatJid) || cap <= 0) return ids;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (remoteId, from, device) in messages)
        {
            if (string.IsNullOrWhiteSpace(remoteId) || !string.Equals(from, chatJid, StringComparison.OrdinalIgnoreCase)) continue;
            if (!GowaAccountPolicy.AppliesTo(device, deviceId)) continue;
            if (!seen.Add(remoteId)) continue;
            ids.Add(remoteId);
            if (ids.Count >= cap) break;
        }
        return ids;
    }
}
