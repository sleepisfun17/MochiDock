using System;
using System.Text.RegularExpressions;

namespace MochiDock.Core;

/// <summary>WhatsApp shows a mention as "@6281234567890" in the text; this swaps the number for the saved or known name.</summary>
public static class MentionResolver
{
    static readonly Regex Mention = new(@"@(\d{6,20})(?!\d)", RegexOptions.Compiled);

    /// <summary>The user part of a JID ("628123:5@s.whatsapp.net" -> "628123"), or "" when it is not a plain number.</summary>
    public static string NumberOf(string? jid)
    {
        if (string.IsNullOrWhiteSpace(jid)) return "";
        string head = jid.Trim();
        int at = head.IndexOf('@');
        if (at >= 0) head = head[..at];
        int colon = head.IndexOf(':');
        if (colon >= 0) head = head[..colon];
        foreach (char c in head) if (c < '0' || c > '9') return "";
        return head;
    }

    /// <summary>True when the text has at least one number mention.</summary>
    public static bool HasMention(string? text) => !string.IsNullOrEmpty(text) && Mention.IsMatch(text);

    /// <summary>Replaces every "@number" the lookup knows with "@name"; unknown numbers stay as they are.</summary>
    public static string Replace(string text, Func<string, string?> lookup)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Mention.Replace(text, m =>
        {
            string? name = lookup(m.Groups[1].Value);
            return string.IsNullOrWhiteSpace(name) ? m.Value : "@" + name.Trim();
        });
    }
}
