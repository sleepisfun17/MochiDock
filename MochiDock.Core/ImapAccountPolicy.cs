using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>An IMAP mailbox MochiDock watches. The password is never part of this type; it lives in the encrypted credential store.</summary>
public sealed class ImapAccount
{
    public string Address { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;
    /// <summary>Sends the IMAP \Seen flag when a mail is marked read in MochiDock. Off by default: it is the only write MochiDock ever makes.</summary>
    public bool MarkAsRead { get; set; }
}

/// <summary>A well-known provider: host, port and where its app password is created.</summary>
public sealed record ImapPreset(string Name, string Host, int Port, string HelpUrl, string WebmailUrl);

/// <summary>Presets, validation and new-mail selection for IMAP accounts. Pure logic, no network.</summary>
public static class ImapAccountPolicy
{
    public static IReadOnlyList<ImapPreset> Presets { get; } = new[]
    {
        new ImapPreset("Gmail", "imap.gmail.com", 993, "https://myaccount.google.com/apppasswords", "https://mail.google.com/mail/?authuser={0}#inbox"),
        new ImapPreset("Yahoo Mail", "imap.mail.yahoo.com", 993, "https://login.yahoo.com/account/security", "https://mail.yahoo.com/"),
        new ImapPreset("iCloud Mail", "imap.mail.me.com", 993, "https://account.apple.com/account/manage", "https://www.icloud.com/mail"),
        new ImapPreset("Fastmail", "imap.fastmail.com", 993, "https://app.fastmail.com/settings/security/integrations", "https://app.fastmail.com/mail/"),
    };

    public static ImapPreset? PresetFor(string host) =>
        Presets.FirstOrDefault(p => string.Equals(p.Host, host?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns an error message, or an empty string when the account can be saved.</summary>
    public static string Validate(string address, string host, int port)
    {
        address = (address ?? "").Trim();
        host = (host ?? "").Trim();
        int at = address.IndexOf('@');
        if (at <= 0 || at != address.LastIndexOf('@') || at == address.Length - 1 || address.Any(char.IsWhiteSpace))
            return "Enter the full email address.";
        if (host.Length == 0 || host.Any(c => char.IsWhiteSpace(c) || c == '/' || c == ':') || !host.Contains('.'))
            return "Enter the IMAP server name, for example imap.gmail.com.";
        if (port < 1 || port > 65535) return "The port must be between 1 and 65535.";
        return "";
    }

    /// <summary>Same mailbox, whatever the letter case.</summary>
    public static bool SameAccount(ImapAccount a, string address) =>
        string.Equals(a.Address?.Trim(), address?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Message ids above the baseline, oldest first and capped, so a mailbox that grew while MochiDock was off never floods the dock.</summary>
    public static IReadOnlyList<uint> NewUids(IEnumerable<uint> uids, uint baseline, int max = 5) =>
        uids.Where(u => u > baseline).OrderBy(u => u).TakeLast(max).ToList();

    /// <summary>
    /// Where the account's app password is revoked: the provider's app-password or security page (the same page that creates one),
    /// or null for a server MochiDock does not know. Removing an account only deletes the copy on this PC; no provider lets an app
    /// revoke its own app password, so the user has to do it there.
    /// </summary>
    public static string? RevokeUrl(ImapAccount account) => PresetFor(account.Host)?.HelpUrl;

    /// <summary>The note shown after an account is removed, reminding that the app password itself still works until revoked.</summary>
    public static string RemovedNotice(ImapAccount account) => PresetFor(account.Host) is { } preset
        ? $"{account.Address.Trim()} was removed from this PC. Its app password still works for your {preset.Name} account until you revoke it there."
        : $"{account.Address.Trim()} was removed from this PC. Its app password still works on your mail server until you revoke it with your provider.";

    /// <summary>
    /// Webmail address to open for a mail: Gmail goes to the right account, others to their inbox, unknown hosts to nothing.
    /// Gmail gets the address as the authuser query value: in the /mail/u/{x}/ path an escaped "@" (%40) is not decoded, and
    /// Gmail answers with "Temporary Error (404)".
    /// </summary>
    public static string? WebmailUrl(ImapAccount account)
    {
        var preset = PresetFor(account.Host);
        if (preset == null) return null;
        return string.Format(preset.WebmailUrl, Uri.EscapeDataString(account.Address.Trim()));
    }
}
