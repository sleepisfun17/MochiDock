using System;
using System.Collections.Generic;

namespace MochiDock.Core;

/// <summary>Detects newsletter and promotion mail. Plain "noreply" senders are deliberately not treated as ads.</summary>
public static class MailAdFilter
{
    static readonly string[] AdKeywords = { "配信停止", "unsubscribe", "メールマガジン", "メルマガ", "PR:", "[広告]" };
    static readonly string[] AdSenders = { "newsletter", "promo@" };

    public static bool IsAdvertisement(string sender, string subject, string body)
    {
        foreach (var s in AdSenders)
            if (sender.Contains(s, StringComparison.OrdinalIgnoreCase)) return true;

        string combined = subject + " " + (body.Length > 300 ? body[..300] : body);
        foreach (var kw in AdKeywords)
            if (combined.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
