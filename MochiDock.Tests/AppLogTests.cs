using MochiDock.Services;
namespace MochiDock.Tests;
[Collection("AppLog")]
public sealed class AppLogTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "AppLogTests-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    public AppLogTests() => AppLog.Configure(dir, () => now);
    public void Dispose() { AppLog.Configure(Path.Combine(Path.GetTempPath(), "AppLogTests-unused")); try { Directory.Delete(dir, true); } catch { } }
    // Other tests exercise services that log too, so assertions look only at this test's own area.
    private static List<AppLog.LogEntry> Mine(string area) => AppLog.Recent().Where(e => e.Area == area).ToList();
    // Tests in other collections run in parallel and may be appending to this file through AppLog at the same moment, so read it
    // without blocking a writer (File.ReadAllText refuses to share with one and failed now and then with "being used by another process").
    private string Text()
    {
        using var stream = new FileStream(Path.Combine(dir, "mochidock.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact] public void WarningIsWrittenAndListed()
    {
        AppLog.Warn("T-Weather", new InvalidOperationException("boom"));
        Assert.Contains("WARN [T-Weather]", Text());
        Assert.Contains("InvalidOperationException: boom", Text());
        Assert.Single(Mine("T-Weather"));
    }

    [Fact] public void RepeatsInsideTheWindowAreSuppressedAndLaterOnesLogged()
    {
        var ex = new IOException("disk");
        AppLog.Warn("T-Hardware", ex); AppLog.Warn("T-Hardware", ex);
        Assert.Single(Mine("T-Hardware"));
        now = now.AddMinutes(2);
        AppLog.Warn("T-Hardware", ex);
        Assert.Equal(2, Mine("T-Hardware").Count);
    }

    [Fact] public void CancellationIsNotAFailure()
    {
        AppLog.Warn("T-Lyrics", new OperationCanceledException());
        AppLog.Warn("T-Lyrics", new TaskCanceledException());
        Assert.Empty(Mine("T-Lyrics"));
    }

    [Theory]
    [InlineData("failed https://x.test/a?api_key=SECRET123&b=1", "SECRET123")]
    [InlineData("Authorization: Bearer abc.def.ghi", "abc.def.ghi")]
    [InlineData("token=top-secret", "top-secret")]
    public void SecretsAreRedacted(string message, string secret)
    {
        AppLog.Warn("T-Net", message);
        Assert.DoesNotContain(secret, Text());
        Assert.Contains("<redacted>", Text());
    }

    [Fact] public void OrdinaryWordsAreNotRedacted()
    {
        AppLog.Warn("T-AI", "Key rejected by provider");
        Assert.Contains("Key rejected by provider", Text());
    }

    [Fact] public void FileRotatesWhenItGrowsTooLarge()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mochidock.log"), new string('x', 600 * 1024));
        AppLog.Warn("T-Rotate", "after");
        Assert.True(File.Exists(Path.Combine(dir, "mochidock.1.log")));
        Assert.Contains("after", Text());
        Assert.True(new FileInfo(Path.Combine(dir, "mochidock.log")).Length < 1024);
    }

    [Fact] public void RecentListIsBounded()
    {
        for (int i = 0; i < 100; i++) AppLog.Warn("T-Spam", "message " + i);
        Assert.True(AppLog.Recent().Count <= 40);
        Assert.True(AppLog.WarningCount >= 100);
    }
}
