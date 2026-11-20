using System;
using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class ScriptReaderEngineTests
{
    [Fact]
    public void CleanScript_NormalizesWhitespaceAndNewlines()
    {
        string raw = "Hello \r\n\r\n  world!   \nThis   is   a   test.  ";
        string cleaned = ScriptReaderEngine.CleanScript(raw);
        Assert.Equal("Hello\n\nworld!\nThis is a test.", cleaned);
    }

    [Fact]
    public void CalculateReadingTime_ComputesAccurateDuration()
    {
        // 300 words at 150 WPM = exactly 2.0 minutes
        var duration = ScriptReaderEngine.CalculateReadingTime(300, 150);
        Assert.Equal(TimeSpan.FromMinutes(2.0), duration);

        // 75 words at 150 WPM = 30 seconds
        var halfMin = ScriptReaderEngine.CalculateReadingTime(75, 150);
        Assert.Equal(TimeSpan.FromSeconds(30), halfMin);
    }

    [Fact]
    public void LineDurationSeconds_EnforcesMinimumDuration()
    {
        // Even 1 word at 300 WPM should take at least MinLineSeconds (0.8s)
        double seconds = ScriptReaderEngine.LineDurationSeconds(1, 300, false);
        Assert.True(seconds >= ScriptReaderEngine.MinLineSeconds);

        // Paragraph end adds ParagraphRestSeconds (0.6s)
        double paraSeconds = ScriptReaderEngine.LineDurationSeconds(1, 300, true);
        Assert.True(paraSeconds >= ScriptReaderEngine.MinLineSeconds + ScriptReaderEngine.ParagraphRestSeconds);
    }
}

