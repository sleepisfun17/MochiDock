using MochiDock.Core;
using MochiDock.Services;

public class GamePlaytimeStoreTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "mochi-playtime-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(folder, "GamePlaytime.json");

    public GamePlaytimeStoreTests() => Directory.CreateDirectory(folder);
    public void Dispose() { try { Directory.Delete(folder, true); } catch (IOException) { } }

    [Fact]
    public async Task PlaytimeRoundTripsAndStaysCaseInsensitive()
    {
        var now = DateTime.Now;
        var store = new GamePlaytimeStore(FilePath);
        store.Add("Valorant", now.AddHours(-1), now);
        store.Add("valorant", now.AddHours(-3), now.AddHours(-2));
        await store.FlushAsync();

        var reloaded = new GamePlaytimeStore(FilePath);
        Assert.Single(reloaded.Ledger.Games);
        Assert.Equal(2 * 3600, reloaded.Ledger.TotalBetween(now.AddDays(-1), now), 1.0);
    }

    [Fact]
    public async Task ClearEmptiesTheFile()
    {
        var store = new GamePlaytimeStore(FilePath);
        store.Add("G", DateTime.Now.AddHours(-1), DateTime.Now);
        store.Clear();
        await store.FlushAsync();
        Assert.Empty(new GamePlaytimeStore(FilePath).Ledger.Games);
    }

    [Fact]
    public async Task AnUnreadableFileStartsEmptyAndIsKept()
    {
        File.WriteAllText(FilePath, "{ not json");
        var store = new GamePlaytimeStore(FilePath);
        Assert.Empty(store.Ledger.Games);
        store.Add("G", DateTime.Now.AddHours(-1), DateTime.Now);
        await store.FlushAsync();
        Assert.True(Directory.GetFiles(folder).Length >= 2, "the unreadable file should be preserved next to the new one");
    }

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(59, "0m")]
    [InlineData(60 * 42, "42m")]
    [InlineData(3600 + 5 * 60, "1h 05m")]
    [InlineData(10 * 3600 + 59 * 60, "10h 59m")]
    public void DurationText(double seconds, string expected) => Assert.Equal(expected, PlaytimeFormat.Duration(seconds));
}
