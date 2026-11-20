using System.Globalization;

namespace MochiDock.Core;

/// <summary>
/// Daily copies of settings.json (settings.backup-yyyyMMdd.json next to it), so a settings file that was overwritten by
/// mistake can be put back by hand. One copy per day, the newest few kept.
/// </summary>
public static class SettingsBackupPolicy
{
    public const int Keep = 3;
    const string Prefix = "settings.backup-", Suffix = ".json", DateFormat = "yyyyMMdd";

    public static string FileName(DateTime day) => Prefix + day.ToString(DateFormat, CultureInfo.InvariantCulture) + Suffix;

    /// <summary>The day a backup file name belongs to, or null for any other file.</summary>
    public static DateTime? DayOf(string fileName) =>
        fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase) &&
        DateTime.TryParseExact(fileName[Prefix.Length..^Suffix.Length], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day : null;

    /// <summary>Backup names to delete so that only the newest <paramref name="keep"/> remain; other names are ignored.</summary>
    public static IReadOnlyList<string> ToDelete(IEnumerable<string> fileNames, int keep = Keep) =>
        fileNames.Select(n => (Name: n, Day: DayOf(n))).Where(x => x.Day != null)
                 .OrderByDescending(x => x.Day).Skip(Math.Max(0, keep)).Select(x => x.Name).ToList();
}
