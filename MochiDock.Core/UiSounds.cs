namespace MochiDock.Core;

/// <summary>
/// MochiDock's short UI sounds, synthesised in code so no audio file has to be shipped or licensed. Each method returns a complete
/// 16-bit mono PCM WAV file (44.1 kHz) that <c>System.Media.SoundPlayer</c> can play from a stream.
/// </summary>
public static class UiSounds
{
    public const int SampleRate = 44100;
    /// <summary>Peak level: about -6 dBFS, so the sounds are clear but never loud.</summary>
    public const double Peak = 0.5;

    /// <summary>Notification "pop" (also the prayer Tone chime): a soft blip that slides down from 1320 Hz to 660 Hz in about 110 ms.</summary>
    public static byte[] Pop()
    {
        const double seconds = 0.11;
        int n = (int)(SampleRate * seconds);
        var samples = new double[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double frequency = 1320 * Math.Pow(0.5, t);           // one octave down over the sound
            phase += 2 * Math.PI * frequency / SampleRate;
            double envelope = Math.Min(1, t / 0.06) * Math.Pow(1 - t, 2.2); // quick attack, smooth decay
            samples[i] = Math.Sin(phase) * envelope;
        }
        return ToWav(samples);
    }

    /// <summary>Petting Mochi: two rising bell-like notes (E6 then A6) with a gentle tail, about 350 ms.</summary>
    public static byte[] Love()
    {
        const double seconds = 0.35;
        int n = (int)(SampleRate * seconds);
        var samples = new double[n];
        AddBell(samples, 1318.5, 0.000, 0.22);
        AddBell(samples, 1760.0, 0.110, 0.24);
        return ToWav(samples);
    }

    // A sine with a soft second harmonic, a 6 ms attack and an exponential decay over its duration.
    static void AddBell(double[] samples, double frequency, double start, double duration)
    {
        int from = (int)(start * SampleRate), length = (int)(duration * SampleRate);
        for (int i = 0; i < length && from + i < samples.Length; i++)
        {
            double t = (double)i / SampleRate;
            double attack = Math.Min(1, t / 0.006);
            double decay = Math.Exp(-5.5 * t / duration) * (1 - (double)i / length);
            double tone = Math.Sin(2 * Math.PI * frequency * t) + 0.25 * Math.Sin(4 * Math.PI * frequency * t);
            samples[from + i] += tone * attack * decay;
        }
    }

    /// <summary>Normalises to <see cref="Peak"/>, fades the last 3 ms to silence and writes a RIFF/WAVE file.</summary>
    static byte[] ToWav(double[] samples)
    {
        double max = samples.Max(Math.Abs);
        double gain = max > 0 ? Peak / max : 0;
        int fade = Math.Min(samples.Length, (int)(0.003 * SampleRate));
        for (int i = 0; i < fade; i++) samples[samples.Length - 1 - i] *= (double)i / fade;

        const short channels = 1, bitsPerSample = 16;
        int dataBytes = samples.Length * channels * bitsPerSample / 8;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);                                   // PCM header size
        writer.Write((short)1);                             // PCM
        writer.Write(channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (double s in samples) writer.Write((short)Math.Round(Math.Clamp(s * gain, -1, 1) * short.MaxValue));
        writer.Flush();
        return stream.ToArray();
    }
}
