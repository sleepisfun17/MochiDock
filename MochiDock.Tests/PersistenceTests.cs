using MochiDock.Services;
using MochiDock.Core;
namespace MochiDock.Tests;
public sealed class PersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MochiDockTests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(root, "settings.json");
    public record Example(int Value);
    [Fact] public void SettingsFileWithRemovedKeysStillLoads()
    {
        const string json = """{"EnableGowa":false,"EnableMascot":false,"AutoNotchInGaming":false,"GamingBottomMargin":30,"GroupNotifications":false,"ShowCaffeineTab":true,"GowaPort":4000}""";
        var settings = System.Text.Json.JsonSerializer.Deserialize<MochiDock.Models.AppSettings>(json);
        Assert.NotNull(settings);
        Assert.Equal(4000, settings!.GowaPort);
    }
    [Fact] public void MissingDoesNotCreateData()
    {
        var result = AtomicJsonStore.Load(FilePath, () => new Example(7));
        Assert.Equal(JsonLoadStatus.Missing, result.Status);
        Assert.False(File.Exists(FilePath));
    }
    [Fact] public void CorruptionPreservesOriginalAndRecoversBackup()
    {
        AtomicJsonStore.Save(FilePath, new Example(1));
        AtomicJsonStore.Save(FilePath, new Example(2));
        File.WriteAllText(FilePath, "broken");
        var result = AtomicJsonStore.Load(FilePath, () => new Example(0));
        Assert.Equal(JsonLoadStatus.Recovered, result.Status);
        Assert.Equal(1, result.Value.Value);
        Assert.Equal("broken", File.ReadAllText(Directory.GetFiles(root, "*.corrupt-*").Single()));
    }
    [Fact] public async Task SettingsServiceLoadRecoversFromBackupAndSurvivesSecondLoad()
    {
        string path = SettingsService.SettingsFilePath;
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        try
        {
            var original = new MochiDock.Models.AppSettings
            {
                WeatherCity = "Osaka",
                CapsuleTheme = "Matcha",
                RemoteWebhookToken = "tok-keep-123"
            };
            SettingsService.Save(original);
            await SettingsService.FlushAsync();
            // Save a second time so .bak holds original
            SettingsService.Save(original);
            await SettingsService.FlushAsync();
            File.WriteAllText(path, "{corrupt");

            var first = SettingsService.Load();
            var second = SettingsService.Load();

            Assert.Equal("Osaka", first.WeatherCity);
            Assert.Equal("Matcha", first.CapsuleTheme);
            Assert.Equal("tok-keep-123", first.RemoteWebhookToken);
            Assert.Equal("Osaka", second.WeatherCity);
            Assert.Equal("Matcha", second.CapsuleTheme);
            Assert.Equal("tok-keep-123", second.RemoteWebhookToken);
            Assert.True(File.Exists(path));
        }
        finally
        {
            await SettingsService.FlushAsync();
            foreach (var f in Directory.EnumerateFiles(dir, "settings.json*"))
                File.Delete(f);
        }
    }
    [Fact] public void LockedFileBlocksAutomaticOverwrite()
    {
        AtomicJsonStore.Save(FilePath, new Example(3));
        using (var handle = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.False(AtomicJsonStore.Load(FilePath, () => new Example(0)).CanWrite);
        Assert.Throws<IOException>(() => AtomicJsonStore.Save(FilePath, new Example(4)));
        Assert.Contains("3", File.ReadAllText(FilePath));
    }
    [Fact] public void InvalidSaveLeavesOriginal()
    {
        AtomicJsonStore.Save(FilePath, new Example(3));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => AtomicJsonStore.SaveJson(FilePath, "{"));
        Assert.Equal(3, AtomicJsonStore.Load(FilePath, () => new Example(0)).Value.Value);
    }
    [Fact] public async Task ConcurrentSavesProduceCompleteJson()
    {
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => AtomicJsonStore.Save(FilePath, new Example(i)))));
        Assert.Equal(JsonLoadStatus.Loaded, AtomicJsonStore.Load(FilePath, () => new Example(-1)).Status);
        Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
    }
    [Fact] public void OldRequestCannotWinAfterReturningToTrack()
    {
        var requests = new RequestGeneration();
        long first = requests.Next(); requests.Next(); long third = requests.Next();
        Assert.False(requests.IsCurrent(first)); Assert.True(requests.IsCurrent(third));
    }
    [Theory] [InlineData(23, true)] [InlineData(6, true)] [InlineData(7, false)] [InlineData(12, false)]
    public void QuietHoursCrossMidnight(int hour, bool expected) => Assert.Equal(expected, NotificationPolicy.IsQuiet(TimeSpan.FromHours(hour),TimeSpan.FromHours(22),TimeSpan.FromHours(7)));
    [Fact] public void QiblaVariesByCoordinates()
    {
        Assert.InRange(PrayerGeometry.QiblaBearing(35.68,139.69),292,295);
        Assert.InRange(PrayerGeometry.QiblaBearing(51.5,-0.12),118,120);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root,true); }
}
