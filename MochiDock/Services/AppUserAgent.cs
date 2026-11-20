using System.Reflection;

namespace MochiDock.Services;

/// <summary>
/// The one User-Agent every MochiDock HTTP request sends: product, version and the project URL, so services that ask for an
/// identifying client (LRCLIB, Nominatim, Open-Meteo) can reach the maintainer. It carries no user or machine details.
/// </summary>
public static class AppUserAgent
{
    public const string ProjectUrl = "https://github.com/sleepisfun17/MochiDock";

    public static string Value { get; } = "MochiDock/" + Version() + " (+" + ProjectUrl + ")";

    static string Version()
    {
        var informational = typeof(AppUserAgent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // The SDK may append "+<commit>"; only the release version belongs in the header.
        var version = informational?.Split('+')[0];
        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
    }
}
