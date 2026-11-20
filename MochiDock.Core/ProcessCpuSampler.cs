namespace MochiDock.Core;
public sealed class ProcessCpuSampler
{
    private readonly Dictionary<(int Pid, long Created), (long Cpu, double Time)> samples = new();
    public double? Sample(int pid, long created, long cpuTicks, double seconds, int processors)
    {
        var key = (pid, created);
        var found = samples.TryGetValue(key, out var old);
        samples[key] = (cpuTicks, seconds);
        if (!found || processors <= 0 || seconds <= old.Time || cpuTicks < old.Cpu) return null;
        return Math.Clamp((cpuTicks-old.Cpu)/10000000.0/(seconds-old.Time)/processors*100,0,100);
    }
    public void Retain(IEnumerable<(int Pid, long Created)> live)
    {
        var keys = live.ToHashSet();
        foreach(var key in samples.Keys.Where(k=>!keys.Contains(k)).ToArray()) samples.Remove(key);
    }
}