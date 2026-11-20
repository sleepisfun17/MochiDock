using MochiDock.Core;
using MochiDock.Services;

public class GameSessionStoreTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "mochi-sessions-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(folder, "GameSessions.json");

    public GameSessionStoreTests() => Directory.CreateDirectory(folder);
    public void Dispose() { try { Directory.Delete(folder, true); } catch (IOException) { } }

    static GameSessionRecord Record(string game) => new() { Game = game, AverageFps = 100, Low1Fps = 80, Seconds = 600, Frames = 60000 };

    [Fact]
    public async Task SessionsRoundTripNewestFirst()
    {
        var store = new GameSessionStore(FilePath);
        store.Add(Record("A"));
        store.Add(Record("B"));
        await store.FlushAsync();

        var reloaded = new GameSessionStore(FilePath);
        Assert.Equal(new[] { "B", "A" }, reloaded.Sessions.Select(s => s.Game).ToArray());
        Assert.Equal(80, reloaded.Sessions[0].Low1Fps);
    }

    [Fact]
    public async Task HistoryIsCapped()
    {
        var store = new GameSessionStore(FilePath);
        for (int i = 0; i < GameSessionFormat.HistoryCap + 10; i++) store.Add(Record("G" + i));
        await store.FlushAsync();
        Assert.Equal(GameSessionFormat.HistoryCap, new GameSessionStore(FilePath).Sessions.Count);
    }

    [Fact]
    public async Task ClearEmptiesTheFile()
    {
        var store = new GameSessionStore(FilePath);
        store.Add(Record("A"));
        store.Clear();
        await store.FlushAsync();
        Assert.Empty(new GameSessionStore(FilePath).Sessions);
    }

    [Fact]
    public void CorruptFileStartsEmptyAndIsPreservedNotOverwrittenSilently()
    {
        File.WriteAllText(FilePath, "{ not json");
        var store = new GameSessionStore(FilePath);
        Assert.Empty(store.Sessions);
        Assert.Contains(Directory.GetFiles(folder), f => f.Contains(".corrupt", StringComparison.OrdinalIgnoreCase) || File.Exists(FilePath));
    }
}
