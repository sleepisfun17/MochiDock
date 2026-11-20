using System.Text.Json;
using MochiDock.Models;

public class TasksTabMigrationTests
{
    static AppSettings Load(string json) => JsonSerializer.Deserialize<AppSettings>(json)!;

    [Fact]
    public void OldFileWithTheTasksTabOnTurnsTodayOn()
    {
        var settings = Load("{\"ShowTasksTab\":true,\"ShowTodayTab\":false}");
        settings.MigrateTasksTab();
        Assert.True(settings.ShowTodayTab);
        Assert.True(settings.TasksTabMerged);
        Assert.False(settings.ExtensionData?.ContainsKey("ShowTasksTab") ?? false);
    }

    [Fact]
    public void FileThatNeverMentionedTheTabHadItOnByDefault()
    {
        var settings = Load("{\"ShowTodayTab\":false}");
        settings.MigrateTasksTab();
        Assert.True(settings.ShowTodayTab);
    }

    [Fact]
    public void SomeoneWhoHadHiddenTheTasksTabKeepsTodayAsItWas()
    {
        var settings = Load("{\"ShowTasksTab\":false,\"ShowTodayTab\":false}");
        settings.MigrateTasksTab();
        Assert.False(settings.ShowTodayTab);
        Assert.True(settings.TasksTabMerged);
    }

    [Fact]
    public void MigrationRunsOnlyOnceSoTurningTodayOffSticks()
    {
        var settings = Load("{\"ShowTasksTab\":true}");
        settings.MigrateTasksTab();
        settings.ShowTodayTab = false;
        settings.MigrateTasksTab();
        Assert.False(settings.ShowTodayTab);
        // A saved file carries the marker, so loading it again does nothing.
        var reloaded = Load(JsonSerializer.Serialize(settings));
        reloaded.MigrateTasksTab();
        Assert.False(reloaded.ShowTodayTab);
    }
}
