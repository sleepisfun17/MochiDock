using System.IO;
using System.Linq;
using MochiDock.Core;
using Xunit;

public class ToolLocatorTests
{
    private const string App = @"C:\App";
    private const string Local = @"C:\Users\u\AppData\Local";

    [Fact]
    public void AppToolsFolderWinsOverLocalAppDataAndPath()
    {
        var all = ToolLocator.FfmpegCandidates(App, Local, @"C:\bin").ToList();
        var found = ToolLocator.FindFfmpeg(App, Local, @"C:\bin", _ => true);
        Assert.Equal(Path.Combine(App, "Tools", "ffmpeg.exe"), found);
        Assert.Equal(Path.Combine(Local, "MochiDock", "Tools", "ffmpeg.exe"), all[1]);
        Assert.Equal(Path.Combine(@"C:\bin", "ffmpeg.exe"), all[2]);
    }

    [Fact]
    public void FallsBackToLocalAppDataThenPath()
    {
        var local = Path.Combine(Local, "MochiDock", "Tools", "ffmpeg.exe");
        Assert.Equal(local, ToolLocator.FindFfmpeg(App, Local, @"C:\bin", p => p == local));
        var onPath = Path.Combine(@"D:\tools", "ffmpeg.exe");
        Assert.Equal(onPath, ToolLocator.FindFfmpeg(App, Local, @"C:\bin;""D:\tools"";;", p => p == onPath));
    }

    [Fact]
    public void ReturnsNullWhenNothingExists() =>
        Assert.Null(ToolLocator.FindFfmpeg(App, Local, null, _ => false));

    [Fact]
    public void BuildArguments_PassesFfmpegLocationOnlyWhenKnown()
    {
        var with = YtDlpCommand.BuildArguments("https://e.com", "d", true, @"C:\App\Tools\ffmpeg.exe");
        var i = with.ToList().IndexOf("--ffmpeg-location");
        Assert.True(i >= 0);
        Assert.Equal(@"C:\App\Tools\ffmpeg.exe", with[i + 1]);
        Assert.DoesNotContain("--ffmpeg-location", YtDlpCommand.BuildArguments("https://e.com", "d", false));
    }

    [Fact]
    public void Video_WithoutFfmpegAvoidsMergeFormat()
    {
        Assert.Contains("best[ext=mp4]/best", YtDlpCommand.BuildArguments("https://e.com", "d", false));
        Assert.Contains(YtDlpCommand.BuildArguments("https://e.com", "d", false, @"C:\f.exe"), a => a.Contains("bestvideo"));
        Assert.True(YtDlpCommand.RequiresFfmpeg(true));
        Assert.False(YtDlpCommand.RequiresFfmpeg(false));
    }
}
