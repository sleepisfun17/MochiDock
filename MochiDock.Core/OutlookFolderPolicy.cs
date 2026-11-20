using System;
using System.Collections.Generic;
using System.Linq;

namespace MochiDock.Core;

/// <summary>Which Outlook folders are watched: every Inbox (with all subfolders) plus any folder whose name the user listed (with all subfolders).</summary>
public static class OutlookFolderPolicy
{
    /// <summary>Used until the user's list arrives (for example in the helper before its config message): every Inbox only.</summary>
    public static readonly string[] DefaultTrackedFolders = Array.Empty<string>();

    /// <summary>Names separated by '|' travel in one pipe field; blanks are dropped.</summary>
    public static string Join(IEnumerable<string>? names) =>
        string.Join("|", Clean(names));

    public static string[] Split(string? joined) =>
        Clean((joined ?? "").Split('|'));

    static string[] Clean(IEnumerable<string>? names) =>
        (names ?? Array.Empty<string>()).Select(n => (n ?? "").Trim()).Where(n => n.Length > 0 && !n.Contains('|'))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// Whether a folder is watched while walking a mail store.
    /// Default scope: a folder below a watched folder, an Inbox, a folder named like an inbox, or a listed folder.
    /// "Only listed" scope: a folder below a watched folder or a listed folder, so an Inbox and its other subfolders are ignored.
    /// </summary>
    public static bool IsWatched(string? name, bool isInbox, bool inside, IEnumerable<string> tracked, bool onlyListed) =>
        onlyListed
            ? inside || IsTrackedName(name, tracked)
            : inside || isInbox || LooksLikeInbox(name) || IsTrackedName(name, tracked);

    /// <summary>True for a folder named like an inbox in English or Japanese.</summary>
    public static bool LooksLikeInbox(string? name) =>
        !string.IsNullOrEmpty(name) &&
        (name.Contains("Inbox", StringComparison.OrdinalIgnoreCase) || name.Contains("受信トレイ", StringComparison.Ordinal));

    /// <summary>True when the folder name matches one of the tracked names exactly (ignoring case and full-width forms).</summary>
    public static bool IsTrackedName(string? name, IEnumerable<string> tracked)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string n = Normalize(name);
        return tracked.Any(t => string.Equals(n, Normalize(t), StringComparison.OrdinalIgnoreCase));
    }

    static string Normalize(string s) => s.Normalize(System.Text.NormalizationForm.FormKC).Trim();
}
