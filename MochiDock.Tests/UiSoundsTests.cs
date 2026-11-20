using System.Text;
using MochiDock.Core;

public class UiSoundsTests
{
    public static TheoryData<string> Sounds => new() { "Pop", "Love" };

    static byte[] Make(string name) => name == "Pop" ? UiSounds.Pop() : UiSounds.Love();

    static short[] Samples(byte[] wav)
    {
        int count = BitConverter.ToInt32(wav, 40) / 2;
        var s = new short[count];
        for (int i = 0; i < count; i++) s[i] = BitConverter.ToInt16(wav, 44 + i * 2);
        return s;
    }

    [Theory, MemberData(nameof(Sounds))]
    public void IsAPlayablePcmWave(string name)
    {
        var wav = Make(name);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));               // PCM
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));               // mono
        Assert.Equal(UiSounds.SampleRate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));
    }

    [Theory]
    [InlineData("Pop", 0.09, 0.13)]
    [InlineData("Love", 0.30, 0.40)]
    public void IsShort(string name, double minSeconds, double maxSeconds)
    {
        double seconds = Samples(Make(name)).Length / (double)UiSounds.SampleRate;
        Assert.InRange(seconds, minSeconds, maxSeconds);
    }

    [Theory, MemberData(nameof(Sounds))]
    public void IsAtAQuietPeakAndStartsAndEndsSilent(string name)
    {
        var s = Samples(Make(name));
        double peak = s.Max(v => Math.Abs((int)v)) / (double)short.MaxValue;
        Assert.InRange(peak, UiSounds.Peak - 0.01, UiSounds.Peak + 0.01);
        Assert.True(Math.Abs((int)s[0]) < 200, "starts with a click");
        Assert.True(Math.Abs((int)s[^1]) < 200, "ends with a click");
    }

    [Fact]
    public void IsTheSameEveryTime() => Assert.Equal(UiSounds.Pop(), UiSounds.Pop());
}
