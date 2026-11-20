using System.Runtime.CompilerServices;
using MochiDock.Services;

/// <summary>
/// Runs before any test: every default data path (settings, log, caches, sealed credentials) goes to a scratch folder, so no test
/// can read or overwrite the real %AppData%\MochiDock or %LocalAppData%\MochiDock of the person running the tests.
/// </summary>
internal static class TestDataIsolation
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "MochiDock.Tests", Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Redirect() => AppDataPaths.RedirectForTests(Root);
}

public class TestDataIsolationTests
{
    [Fact]
    public void SettingsAndLogLiveInTheScratchFolder()
    {
        Assert.True(AppDataPaths.IsRedirected);
        Assert.StartsWith(TestDataIsolation.Root, SettingsService.SettingsFilePath, StringComparison.OrdinalIgnoreCase);
        // AppLog's own tests point the log at their temp folders while they run, so check the default it starts from instead.
        Assert.StartsWith(TestDataIsolation.Root, Path.Combine(AppDataPaths.Local, "logs"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(TestDataIsolation.Root, AppDataPaths.Local, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfiguringWhatsAppNeverWritesSettings()
    {
        // A blank settings object used to be saved over settings.json when it had no webhook token.
        var settings = new MochiDock.Models.AppSettings();
        var service = new GowaService();
        service.ConfigureFromSettings(settings);
        Assert.False(string.IsNullOrEmpty(settings.RemoteWebhookToken));
        SettingsService.FlushAsync().GetAwaiter().GetResult();
        // Other tests may save settings to the scratch folder in parallel; this object (with its fresh token) must not be among them.
        string path = SettingsService.SettingsFilePath;
        Assert.False(File.Exists(path) && File.ReadAllText(path).Contains(settings.RemoteWebhookToken));
    }
}
