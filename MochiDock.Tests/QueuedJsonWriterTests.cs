using MochiDock.Services;
using Xunit;

namespace MochiDock.Tests;

public class QueuedJsonWriterTests
{
    [Fact]
    public async Task EnqueueAndFlush_WritesSnapshotsInOrderAndCompletesCleanly()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mochidock-qjw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "state.json");
        try
        {
            var writer = new QueuedJsonWriter();
            for (int i = 1; i <= 25; i++)
                writer.Enqueue(path, new { Step = i });

            await writer.FlushAsync();

            string finalText = File.ReadAllText(path);
            Assert.Contains("\"Step\": 25", finalText);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
