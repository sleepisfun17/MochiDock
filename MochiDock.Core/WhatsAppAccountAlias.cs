using System.Text.RegularExpressions;

namespace MochiDock.Core;

/// <summary>
/// A short name the user gives a linked WhatsApp account (for example "JP" and "ID" for two accounts both called "irfan"). It is shown after
/// the sender when several accounts are linked, in place of the account's own label (its WhatsApp name, or its GOWA device id when the names
/// are the same). Aliases are kept in MochiDock's settings by device id, so the GOWA server and its device ids stay untouched.
/// </summary>
public static class WhatsAppAccountAlias
{
    public const int MaxLength = 12;

    /// <summary>The alias as stored: spaces collapsed, trimmed, at most <see cref="MaxLength"/> characters; "" means none.</summary>
    public static string Normalize(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return "";
        string s = Regex.Replace(alias.Trim(), @"\s+", " ");
        return s.Length > MaxLength ? s.Substring(0, MaxLength).TrimEnd() : s;
    }

    /// <summary>The alias for a device id (device ids are matched without regard to case), or null when there is none.</summary>
    public static string? Find(IReadOnlyDictionary<string, string>? aliases, string? deviceId)
    {
        if (aliases == null || string.IsNullOrWhiteSpace(deviceId)) return null;
        foreach (var pair in aliases)
        {
            if (!string.Equals(pair.Key?.Trim(), deviceId.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            string alias = Normalize(pair.Value);
            return alias.Length > 0 ? alias : null;
        }
        return null;
    }

    /// <summary>The label to show for an account: its alias when it has one, else its own label.</summary>
    public static string Label(IReadOnlyDictionary<string, string>? aliases, string? deviceId, string ownLabel) =>
        Find(aliases, deviceId) ?? ownLabel;
}
