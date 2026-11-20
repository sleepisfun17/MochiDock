using System.IO;

namespace MochiDock.Services;

/// <summary>
/// MochiDock's data folders: %AppData%\MochiDock (settings) and %LocalAppData%\MochiDock (caches, logs, sessions, sealed secrets).
/// Every default data path goes through here so the unit tests can redirect all of them to a scratch folder before anything runs:
/// a test must never read or overwrite the user's real settings, log or credentials.
/// </summary>
public static class AppDataPaths
{
    private static string? _testRoot;

    public static string Roaming => _testRoot != null
        ? Path.Combine(_testRoot, "Roaming", "MochiDock")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MochiDock");

    public static string Local => _testRoot != null
        ? Path.Combine(_testRoot, "Local", "MochiDock")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MochiDock");

    /// <summary>True when the paths point at a test scratch folder.</summary>
    public static bool IsRedirected => _testRoot != null;

    /// <summary>Tests only (called from the test assembly's module initializer): sends every default data path under <paramref name="root"/>.</summary>
    public static void RedirectForTests(string root) => _testRoot = Path.GetFullPath(root);
}
