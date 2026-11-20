using MochiDock.Core;

public class MascotTurnTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(360)]
    [InlineData(MascotTurn.TotalDegrees)]
    public void StartAndEndAreTheRestingMochi(double angle)
    {
        var f = MascotTurn.At(angle);
        Assert.Equal((1d, 1d, 1d, 0d, 1d, 0d), (f.BodyScaleX, f.BodyScaleY, f.FaceScaleX, f.FaceShiftX, f.FaceOpacity, f.ShadeShift));
    }

    [Fact]
    public void HeNeverGoesFlat()
    {
        for (double a = 0; a <= MascotTurn.TotalDegrees; a += 5)
        {
            var f = MascotTurn.At(a);
            Assert.InRange(f.BodyScaleX, MascotTurn.Depth, 1);
            Assert.InRange(f.BodyScaleY, 0.97, 1);
        }
        Assert.Equal(MascotTurn.Depth, MascotTurn.At(90).BodyScaleX, 6);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(75)]
    [InlineData(120)]
    public void TheTurnIsSymmetricAroundHisBack(double offset)
    {
        var a = MascotTurn.At(MascotTurn.FaceSwapAt - offset);
        var b = MascotTurn.At(MascotTurn.FaceSwapAt + offset);
        Assert.Equal(a.BodyScaleX, b.BodyScaleX, 6);
        Assert.Equal(a.FaceOpacity, b.FaceOpacity, 6);
        Assert.Equal(a.FaceShiftX, -b.FaceShiftX, 6);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(180)]
    [InlineData(225)]
    [InlineData(270)]
    public void TheFaceIsHiddenOnHisBack(double angle) => Assert.Equal(0, MascotTurn.At(angle).FaceOpacity);

    [Fact]
    public void HeSpinsSeveralTimesAndTheNewFaceGoesOnAtHisBack()
    {
        Assert.Equal(3, MascotTurn.Turns);
        Assert.Equal(1080, MascotTurn.TotalDegrees);
        Assert.Equal(0, MascotTurn.At(MascotTurn.FaceSwapAt).FaceOpacity);   // nobody sees the swap
        Assert.InRange(MascotTurn.FaceSwapAt, MascotTurn.TotalDegrees / 2, MascotTurn.TotalDegrees - 360);
    }

    [Fact]
    public void TheSpinBlurIsZeroAtRestAndStrongestInTheMiddle()
    {
        Assert.Equal(0, MascotTurn.SpinBlur(0));
        Assert.Equal(0, MascotTurn.SpinBlur(MascotTurn.TotalDegrees));
        Assert.Equal(1, MascotTurn.SpinBlur(MascotTurn.TotalDegrees / 2), 6);
        Assert.True(MascotTurn.SpinBlur(200) < MascotTurn.SpinBlur(600));
        Assert.Equal(0, MascotTurn.SpinBlur(-10));
    }

    [Fact]
    public void TheFaceRidesTowardTheEdgeAndNarrows()
    {
        var f = MascotTurn.At(45);
        Assert.True(f.FaceShiftX > 5 && f.FaceShiftX < MascotTurn.FaceTravel);
        Assert.InRange(f.FaceScaleX, 0.70, 0.72);
        Assert.Equal(1, f.FaceOpacity);
        Assert.Equal(1, MascotTurn.At(10).FaceOpacity);
        Assert.True(MascotTurn.At(330).FaceShiftX < 0);   // coming round from the other side
    }
}
