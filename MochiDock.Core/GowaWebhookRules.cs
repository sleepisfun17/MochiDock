using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MochiDock.Core;

// This file is compiled into MochiDock.Core and, as a linked source file, into the webhook relay (tools/gowa-relay): keep it free of dependencies.

/// <summary>
/// Pure rules of the GOWA webhook relay: which target addresses are valid, how a target is written to the log (never with its secret token),
/// and which request headers are passed on.
/// </summary>
public static class WebhookRelayRules
{
    /// <summary>The same rule the installer uses for a webhook address: plain http, no spaces, quotes or commas.</summary>
    private static readonly Regex TargetPattern = new(@"^http://[^\s"",]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TokenValue = new(@"(?<=[?&]token=)[^&#\s]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Headers that belong to one HTTP hop and must not be copied to the forwarded request.
    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Connection", "Keep-Alive", "Transfer-Encoding", "Expect", "Proxy-Connection", "Upgrade", "TE", "Trailer",
    };

    public static bool IsValidTarget(string? url) => !string.IsNullOrWhiteSpace(url) && TargetPattern.IsMatch(url.Trim()) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out _);

    /// <summary>
    /// The targets in a config file: one address per line, blank lines and lines starting with '#' ignored, invalid lines skipped
    /// (they are returned in <paramref name="rejected"/>), duplicates removed in order.
    /// </summary>
    public static IReadOnlyList<string> ParseTargets(IEnumerable<string> lines, out IReadOnlyList<string> rejected)
    {
        var targets = new List<string>();
        var bad = new List<string>();
        foreach (var raw in lines)
        {
            var line = (raw ?? "").Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (!IsValidTarget(line)) { bad.Add(RedactToken(line)); continue; }
            if (!targets.Contains(line, StringComparer.OrdinalIgnoreCase)) targets.Add(line);
        }
        rejected = bad;
        return targets;
    }

    public static IReadOnlyList<string> ParseTargets(IEnumerable<string> lines) => ParseTargets(lines, out _);

    /// <summary>The address with the value of its token parameter replaced by "***", for logs and messages.</summary>
    public static string RedactToken(string? url) => string.IsNullOrEmpty(url) ? "" : TokenValue.Replace(url, "***");

    /// <summary>"host:port" of a target, for one-line log entries (no path, no query, so never a token).</summary>
    public static string Describe(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "(invalid address)";
        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }

    /// <summary>True for headers worth passing on to the targets (content type, signatures); false for per-hop headers.</summary>
    public static bool ShouldForwardHeader(string? name) => !string.IsNullOrWhiteSpace(name) && !HopByHop.Contains(name!);
}

/// <summary>What a GOWA read receipt may clear: nothing, only the notifications whose message ids it names, or the whole chat.</summary>
public enum ReceiptDismissal { None, ByIds, WholeChat }

/// <summary>Which GOWA read receipts clear a notification, and how much.</summary>
public static class AckReceiptPolicy
{
    /// <summary>
    /// "read-self": WhatsApp says this account read the chat on another device, so the notifications go: by message ids when the receipt has them,
    /// otherwise the whole chat. "read": GOWA sends it both when you read an incoming message and when a contact reads one of yours, and the
    /// payload does not say which. It is therefore only trusted by ids: only a notification whose message id is named is removed, and a contact
    /// reading your own message names ids that never match an incoming notification. Without ids a "read" never clears anything, and
    /// never a whole chat. An explicit <paramref name="isFromMe"/> == true (a payload that says the message was yours) never clears.
    /// </summary>
    public static ReceiptDismissal Decide(string? receiptType, bool? isFromMe, bool hasIds)
    {
        if (isFromMe == true) return ReceiptDismissal.None;
        if (string.Equals(receiptType, "read-self", StringComparison.OrdinalIgnoreCase)) return hasIds ? ReceiptDismissal.ByIds : ReceiptDismissal.WholeChat;
        if (string.Equals(receiptType, "read", StringComparison.OrdinalIgnoreCase)) return hasIds ? ReceiptDismissal.ByIds : ReceiptDismissal.None;
        return ReceiptDismissal.None;
    }

    /// <summary>True for any receipt that proves the read-sync path works (a read of either kind).</summary>
    public static bool IsReadReceipt(string? receiptType) =>
        string.Equals(receiptType, "read", StringComparison.OrdinalIgnoreCase) || string.Equals(receiptType, "read-self", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The one-line read-sync status shown in the WhatsApp settings.</summary>
public static class ReadSyncStatusText
{
    public static string Build(DateTime? lastReadReceiptUtc, DateTime? lastReceiptUtc, int rejectedCalls, DateTime nowUtc)
    {
        string text;
        if (lastReadReceiptUtc is { } read) text = $"Last read receipt: {read.ToLocalTime():HH:mm} ({Age(nowUtc - read)}).";
        else if (lastReceiptUtc is { } any) text = $"Receipts are arriving (last at {any.ToLocalTime():HH:mm}), but no read receipt yet since MochiDock started.";
        else text = "No read receipt received since MochiDock started.";
        if (rejectedCalls > 0)
            text += $" {rejectedCalls} call{(rejectedCalls == 1 ? "" : "s")} rejected (token mismatch: re-run the server script with this PC's current address).";
        return text;
    }

    private static string Age(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} h ago";
        return $"{(int)age.TotalDays} d ago";
    }
}
