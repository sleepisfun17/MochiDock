namespace MochiDock.Core;
public static class TabMotion
{
    public static int Direction(int from, int to) => from < 0 || to < 0 ? 0 : Math.Sign(to - from);
    public static (double Outgoing, double Incoming) Opacity(double ms)
    {
        double S(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
        return (1 - S(ms / 104), S((ms - 26) / 130));
    }
}
