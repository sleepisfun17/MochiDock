using System.Text.Json;
using MochiDock.Models;
using MochiDock.Services;

public class WhatsAppOptInTests
{
    static AppSettings Load(string json) => JsonSerializer.Deserialize<AppSettings>(json)!;

    [Fact]
    public void NewSettingsHaveWhatsAppOff()
    {
        Assert.False(new AppSettings().EnableWhatsApp);
    }

    [Fact]
    public void OldFileWithALinkedDeviceKeepsWhatsAppOn()
    {
        var settings = Load("{\"GowaPort\":3099}");
        settings.MigrateWhatsAppOptIn(hadGowaSession: true);
        Assert.True(settings.EnableWhatsApp);
        Assert.True(settings.WhatsAppOptInMigrated);
    }

    [Fact]
    public void OldFileUsingARemoteServerKeepsWhatsAppOn()
    {
        var settings = Load("{\"GowaMode\":\"Remote\"}");
        settings.MigrateWhatsAppOptIn(hadGowaSession: false);
        Assert.True(settings.EnableWhatsApp);
    }

    [Fact]
    public void OldFileWithoutAnyWhatsAppUseStartsOff()
    {
        var settings = Load("{\"ShowWeatherTab\":true}");
        settings.MigrateWhatsAppOptIn(hadGowaSession: false);
        Assert.False(settings.EnableWhatsApp);
    }

    [Fact]
    public void MigrationRunsOnlyOnce()
    {
        var settings = Load("{}");
        settings.MigrateWhatsAppOptIn(false);
        settings.MigrateWhatsAppOptIn(true);
        Assert.False(settings.EnableWhatsApp);
    }

    [Fact]
    public void DisabledServiceStartsNothing()
    {
        var service = new GowaService();
        service.ConfigureFromSettings(new AppSettings());
        service.Start();
        Assert.False(service.Enabled);
        Assert.False(service.IsHealthy);
        service.Stop();
    }
}
