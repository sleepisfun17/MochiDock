namespace MochiDock.Core;

/// <summary>
/// When Mochi turns round (front, back, front) on a change of face. Every real expression change spins, except the first face drawn,
/// a re-apply of the same face, the Idle/Waiting alternation that comes with every idle thought, and swaps between the gaming faces
/// mid-game: those happen every few seconds and a spin each time would be tiring.
/// </summary>
public static class MascotSpinPolicy
{
    public static bool ShouldSpin(MascotMood? from, MascotMood to)
    {
        if (from is not { } previous || previous == to) return false;
        if (IsIdleBlink(previous) && IsIdleBlink(to)) return false;
        if (IsGamingFace(previous) && IsGamingFace(to)) return false;
        return true;
    }

    static bool IsIdleBlink(MascotMood mood) => mood is MascotMood.Idle or MascotMood.Waiting;

    static bool IsGamingFace(MascotMood mood) => mood is MascotMood.Focused or MascotMood.Worried or MascotMood.BreakTime;
}
