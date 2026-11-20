namespace MochiDock.Core;

/// <summary>One frame of Mochi's turn: how to draw the body, the face and the shading at a given angle.</summary>
public readonly record struct MascotTurnFrame(double BodyScaleX, double BodyScaleY, double FaceScaleX, double FaceShiftX, double FaceOpacity, double ShadeShift);

/// <summary>
/// Mochi's turn round his vertical axis, drawn as a 3D object without a 3D scene. He is a thick rounded puck, not a card: turned by
/// θ, a body W wide and D deep shows W|cos θ| + D|sin θ|, so he never goes flat. His face sits on the front surface: it rides toward
/// the edge (sin θ) and narrows (|cos θ|) as it turns away, and is hidden on the back. The light does not move, so the shading
/// shifts with sin θ. Pure, so the frames are unit-tested.
/// </summary>
public static class MascotTurn
{
    /// <summary>Depth as a share of the width: the narrowest Mochi gets (edge-on).</summary>
    public const double Depth = 0.8;

    /// <summary>How far (in the 30-unit body) the face slides round toward the edge at 90°.</summary>
    public const double FaceTravel = 8;

    /// <summary>A change of face spins him this many times, fast, like a transformation.</summary>
    public const int Turns = 3;

    /// <summary>The whole spin in degrees.</summary>
    public const double TotalDegrees = Turns * 360;

    /// <summary>The angle at which the new face goes on: his back faces the viewer in the middle of the second turn, so the new face
    /// comes round for the last turn and a half.</summary>
    public const double FaceSwapAt = 540;

    /// <summary>
    /// How fast he is turning at an angle of the spin, from 0 (at rest, the start and the end) to 1 (the middle, where the eased spin
    /// is fastest). The face fades a little with it: at full speed it strobes softly instead of flashing.
    /// </summary>
    public static double SpinBlur(double degrees) => Round(Math.Sin(Math.PI * Math.Clamp(degrees / TotalDegrees, 0, 1)));

    public static MascotTurnFrame At(double degrees)
    {
        double radians = degrees * Math.PI / 180;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        double absCos = Math.Abs(cos), absSin = Math.Abs(sin);
        double bodyX = Math.Clamp(absCos + Depth * absSin, Depth, 1);
        double bodyY = 1 - 0.02 * absSin;                                     // a little weight at edge-on
        double faceOpacity = Math.Clamp(cos / 0.25, 0, 1);                     // fades out just before the edge, hidden on the back
        return new MascotTurnFrame(Round(bodyX), Round(bodyY), Round(absCos), Round(sin * FaceTravel), Round(faceOpacity), Round(sin * 0.25));
    }

    static double Round(double value) => Math.Abs(value) < 1e-9 ? 0 : Math.Round(value, 6);
}
