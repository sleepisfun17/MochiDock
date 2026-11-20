using System;
using System.Linq;
using System.Text.Json;

namespace MochiDock.Core;

/// <summary>A release version such as <c>0.1.0-beta</c> (a leading "v" and <c>+build</c> metadata are accepted and ignored).</summary>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, string Pre) : IComparable<ReleaseVersion>
{
    public bool IsPrerelease => Pre.Length > 0;

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = new ReleaseVersion(0, 0, 0, "");
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        int plus = t.IndexOf('+');
        if (plus >= 0) t = t[..plus];
        string pre = "";
        int dash = t.IndexOf('-');
        if (dash >= 0) { pre = t[(dash + 1)..]; t = t[..dash]; if (pre.Length == 0) return false; }
        var parts = t.Split('.');
        if (parts.Length is < 1 or > 3) return false;
        var numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out numbers[i])) return false;
        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        // A release is newer than any of its pre-releases (1.0.0 > 1.0.0-beta).
        if (Pre.Length == 0 || other.Pre.Length == 0) return (Pre.Length == 0 ? 1 : 0) - (other.Pre.Length == 0 ? 1 : 0);
        var a = Pre.Split('.');
        var b = other.Pre.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool an = int.TryParse(a[i], out int ai), bn = int.TryParse(b[i], out int bi);
            c = an && bn ? ai.CompareTo(bi) : an ? -1 : bn ? 1 : string.Compare(a[i], b[i], StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }

    public static bool operator >(ReleaseVersion l, ReleaseVersion r) => l.CompareTo(r) > 0;
    public static bool operator <(ReleaseVersion l, ReleaseVersion r) => l.CompareTo(r) < 0;
    public static bool operator >=(ReleaseVersion l, ReleaseVersion r) => l.CompareTo(r) >= 0;
    public static bool operator <=(ReleaseVersion l, ReleaseVersion r) => l.CompareTo(r) <= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Pre.Length > 0 ? "-" + Pre : "");
}

/// <summary>A newer release, the page to download it from and, when the release carries a verifiable setup, that setup.</summary>
public sealed record UpdateOffer(string Version, string Url, UpdateInstallerAsset? Installer = null);

/// <summary>The release's setup file: where to download it, its SHA-256 (GitHub's asset digest) and its size in bytes.</summary>
public sealed record UpdateInstallerAsset(string Name, string DownloadUrl, string Sha256, long Size);

/// <summary>Picks the update offer out of GitHub's release list. Pure: no network.</summary>
public static class UpdateFeed
{
    /// <summary>Only release pages of this project are ever offered, whatever the feed says.</summary>
    public const string ReleasePrefix = "https://github.com/sleepisfun17/MochiDock/";

    /// <summary>Setup files are only ever downloaded from this project's release downloads.</summary>
    public const string DownloadPrefix = "https://github.com/sleepisfun17/MochiDock/releases/download/";

    /// <summary>The size a real setup can have; anything outside is not offered for installing.</summary>
    public const long MinInstallerBytes = 1L << 20, MaxInstallerBytes = 400L << 20;

    /// <summary>The setup's file name for a version, as the release must name its asset.</summary>
    public static string InstallerName(string version) => $"MochiDock-Setup-{version}.exe";

    /// <summary>
    /// The newest published release that is newer than <paramref name="currentVersion"/>, or null when this build is current.
    /// Drafts are ignored; pre-releases are offered only to a build that is itself a pre-release.
    /// Throws <see cref="JsonException"/> when the feed is not a release list.
    /// </summary>
    public static UpdateOffer? Newest(string json, string currentVersion)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out var current)) return null;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Expected a list of releases.");

        ReleaseVersion? best = null;
        string bestUrl = "";
        UpdateInstallerAsset? bestInstaller = null;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (Flag(item, "draft")) continue;
            if (!item.TryGetProperty("tag_name", out var tag) || !ReleaseVersion.TryParse(tag.GetString(), out var v)) continue;
            if ((Flag(item, "prerelease") || v.IsPrerelease) && !current.IsPrerelease) continue;
            string url = item.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            if (!url.StartsWith(ReleasePrefix, StringComparison.Ordinal)) continue;
            if (best is null || v > best) { best = v; bestUrl = url; bestInstaller = Installer(item, v.ToString()); }
        }
        return best is not null && best > current ? new UpdateOffer(best.ToString(), bestUrl, bestInstaller) : null;
    }

    /// <summary>
    /// The release's setup, or null unless every check passes: the asset is named MochiDock-Setup-&lt;version&gt;.exe, it downloads from
    /// this project's releases, GitHub reports its SHA-256 digest, and its size is plausible. Without one the user installs by hand.
    /// </summary>
    private static UpdateInstallerAsset? Installer(JsonElement release, string version)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        string expected = InstallerName(version);
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;
            if (Text(asset, "name") != expected) continue;
            string url = Text(asset, "browser_download_url");
            if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal) || !url.EndsWith("/" + expected, StringComparison.Ordinal)) return null;
            string digest = Text(asset, "digest");
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal)) return null;
            string hash = digest["sha256:".Length..].ToLowerInvariant();
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) return null;
            long size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n) ? n : 0;
            if (size < MinInstallerBytes || size > MaxInstallerBytes) return null;
            return new UpdateInstallerAsset(expected, url, hash, size);
        }
        return null;
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static bool Flag(JsonElement item, string name) =>
        item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}

/// <summary>Whether a running copy is the one the setup installed (only that copy updates itself).</summary>
public static class InstallLocation
{
    /// <summary>True when <paramref name="baseDirectory"/> is <paramref name="installLocation"/> (case and trailing slashes ignored).</summary>
    public static bool Matches(string? installLocation, string? baseDirectory)
    {
        static string Norm(string? p) => (p ?? "").Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
        string a = Norm(installLocation), b = Norm(baseDirectory);
        return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
