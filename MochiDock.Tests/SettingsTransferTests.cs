using System.Text.Json;
using System.Text.Json.Nodes;
using MochiDock.Core;
using MochiDock.Models;
using MochiDock.Services;

public class SettingsTransferTests
{
    static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void LocalOnlyKeysAreRealAppSettingsProperties()
    {
        var serialized = JsonSerializer.SerializeToNode(new AppSettings())!.AsObject();
        foreach (var key in SettingsTransfer.LocalOnlyKeys)
            Assert.True(serialized.ContainsKey(key), $"{key} is not a serialized AppSettings key");
    }

    [Fact]
    public void StripRemovesSecretsAndPcValuesAndKeepsTheRest()
    {
        var settings = JsonSerializer.SerializeToNode(new AppSettings { RemoteWebhookToken = "secret", TargetMonitor = @"\\.\DISPLAY2", WeatherCity = "Osaka" })!.AsObject();
        SettingsTransfer.Strip(settings);
        foreach (var key in SettingsTransfer.LocalOnlyKeys) Assert.False(settings.ContainsKey(key), key);
        Assert.Equal("Osaka", (string?)settings["WeatherCity"]);
        Assert.DoesNotContain("secret", settings.ToJsonString());
    }

    [Fact]
    public void MergeKeepsThisPcsValuesAndDropsTheFilesOnes()
    {
        var imported = Obj("""{"WeatherCity":"Osaka","TopMargin":40,"GowaPort":1234,"TargetMonitor":"other"}""");
        var current = Obj("""{"WeatherCity":"Tokyo","TopMargin":4,"RemoteWebhookToken":"mine","PowerComponents":[{"Name":"GPU"}]}""");
        SettingsTransfer.Merge(imported, current);

        Assert.Equal("Osaka", (string?)imported["WeatherCity"]);
        Assert.Equal(4, (double)imported["TopMargin"]!);
        Assert.Equal("mine", (string?)imported["RemoteWebhookToken"]);
        Assert.Equal("GPU", (string?)imported["PowerComponents"]![0]!["Name"]);
        // Not set on this PC: removed, so the default applies instead of the other PC's value.
        Assert.False(imported.ContainsKey("GowaPort"));
        Assert.False(imported.ContainsKey("TargetMonitor"));
        // The current object is copied, not moved.
        Assert.Equal("GPU", (string?)current["PowerComponents"]![0]!["Name"]);
    }

    [Fact]
    public void MergeWithoutCurrentSettingsFallsBackToDefaults()
    {
        var imported = Obj("""{"TopMargin":40,"RemoteWebhookToken":"theirs"}""");
        SettingsTransfer.Merge(imported, null);
        Assert.False(imported.ContainsKey("TopMargin"));
        Assert.False(imported.ContainsKey("RemoteWebhookToken"));
    }

    [Fact]
    public void ExportRoundTripsSettingsAndTasks()
    {
        var settings = Obj("""{"WeatherCity":"Osaka","RemoteWebhookToken":"secret"}""");
        var tasks = new JsonArray(Obj("""{"Id":"a","Title":"Milk"}"""));
        string file = SettingsTransfer.BuildExport(settings, tasks).ToJsonString();

        Assert.Contains(SettingsTransfer.Format, file);
        Assert.DoesNotContain("secret", file);
        Assert.Equal("secret", (string?)settings["RemoteWebhookToken"]); // the caller's object is left alone

        var (parsed, parsedTasks) = SettingsTransfer.Parse(file);
        Assert.Equal("Osaka", (string?)parsed["WeatherCity"]);
        Assert.Equal("Milk", (string?)parsedTasks![0]!["Title"]);
    }

    [Fact]
    public void PlainSettingsFileIsReadAsSettingsWithoutTasks()
    {
        var (settings, tasks) = SettingsTransfer.Parse("""{"WeatherCity":"Osaka","SomeRemovedKey":true}""");
        Assert.Null(tasks);
        Assert.Equal("Osaka", (string?)settings["WeatherCity"]);
    }

    [Theory]
    [InlineData("""{"format":"MochiDock-export/2","settings":{}}""")]
    [InlineData("""{"format":"SomethingElse","settings":{}}""")]
    [InlineData("""{"format":"MochiDock-export/1"}""")]
    [InlineData("""{"format":"MochiDock-export/1","settings":{},"tasks":{}}""")]
    [InlineData("""{"format":"MochiDock-export/1","settings":{},"tasks":[1]}""")]
    [InlineData("""[]""")]
    public void RejectsFilesItCannotRead(string json) =>
        Assert.ThrowsAny<JsonException>(() => SettingsTransfer.Parse(json));

    [Fact]
    public void OldSettingsFileWithUnknownKeysImports()
    {
        string path = Path.Combine(TestDataIsolation.Root, "import-" + Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(TestDataIsolation.Root);
        File.WriteAllText(path, """{"WeatherCity":"Osaka","EnableTasksTab":true,"NoLongerAKey":42,"PrayerSoundMode":"Tone"}""");

        var import = SettingsService.ReadImport(path);

        Assert.Null(import.Tasks);
        Assert.Equal("Osaka", import.Settings.WeatherCity);
        Assert.False(string.IsNullOrWhiteSpace(import.Settings.RemoteWebhookToken));
    }

    [Fact]
    public void ExportFileImportsSettingsAndHandsBackTasks()
    {
        string path = Path.Combine(TestDataIsolation.Root, "import-" + Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(TestDataIsolation.Root);
        var export = SettingsTransfer.BuildExport(Obj("""{"WeatherCity":"Osaka","PrayerSoundMode":"Tone"}"""),
                                                  new JsonArray(Obj("""{"Id":"a","Title":"Milk"}""")));
        File.WriteAllText(path, export.ToJsonString());

        var import = SettingsService.ReadImport(path);

        Assert.Equal("Osaka", import.Settings.WeatherCity);
        Assert.Single(import.Tasks!);
    }

    sealed record Item(string Id, string Title);

    [Fact]
    public void TaskMergeUpdatesAddsAndKeepsLocalTasks()
    {
        var local = new[] { new Item("a", "Old A"), new Item("b", "Only here") };
        var imported = new[] { new Item("c", "New C"), new Item("a", "New A") };

        var merged = SettingsTransfer.MergeById(local, imported, i => i.Id);

        Assert.Equal(new[] { "c", "a", "b" }, merged.Select(i => i.Id));
        Assert.Equal("New A", merged.Single(i => i.Id == "a").Title);
        Assert.Equal("Only here", merged.Single(i => i.Id == "b").Title);
    }

    [Fact]
    public void TaskMergeUsesTheLastCopyOfARepeatedId()
    {
        var merged = SettingsTransfer.MergeById(Array.Empty<Item>(), new[] { new Item("x", "first"), new Item("x", "second") }, i => i.Id);
        Assert.Equal("second", Assert.Single(merged).Title);
    }
}
