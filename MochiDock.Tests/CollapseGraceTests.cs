using MochiDock.Core;

public class CollapseGraceTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void AStillCursorJustOutsideTheShrunkDockKeepsItOpen()
    {
        Assert.Equal(CollapseGraceDecision.Wait, CollapseGrace.Decide(Ms(800), cursorMovedDip: 0, distanceFromDockDip: 30));
        Assert.Equal(CollapseGraceDecision.Wait, CollapseGrace.Decide(Ms(3900), cursorMovedDip: 0, distanceFromDockDip: 80));
    }

    [Fact]
    public void SmallJitterNearTheOldEdgeDoesNotClose()
    {
        Assert.Equal(CollapseGraceDecision.Wait, CollapseGrace.Decide(Ms(1500), cursorMovedDip: 12, distanceFromDockDip: 40));
        // Moved a lot, but toward the dock (still close to it): the user is coming back, not leaving.
        Assert.Equal(CollapseGraceDecision.Wait, CollapseGrace.Decide(Ms(1500), cursorMovedDip: 60, distanceFromDockDip: 10));
    }

    [Fact]
    public void MovingClearlyAwayCloses()
    {
        Assert.Equal(CollapseGraceDecision.Collapse, CollapseGrace.Decide(Ms(300), cursorMovedDip: 55, distanceFromDockDip: 70));
    }

    [Fact]
    public void AnAbandonedDockClosesAfterTheCap()
    {
        Assert.Equal(CollapseGraceDecision.Collapse, CollapseGrace.Decide(CollapseGrace.MaxWait, cursorMovedDip: 0, distanceFromDockDip: 30));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(450, true)]
    [InlineData(500, false)]
    [InlineData(2000, false)]
    [InlineData(-10, false)]
    public void OnlyALeaveRightAfterAShrinkGetsTheGrace(int sinceShrinkMs, bool grace)
    {
        Assert.Equal(grace, CollapseGrace.IsShrinkLeave(Ms(sinceShrinkMs)));
    }
}
