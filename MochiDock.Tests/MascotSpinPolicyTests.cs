using MochiDock.Core;

public class MascotSpinPolicyTests
{
    [Theory]
    [InlineData(MascotMood.Music, MascotMood.Excited)]
    [InlineData(MascotMood.Idle, MascotMood.Electric)]
    [InlineData(MascotMood.Electric, MascotMood.Idle)]
    [InlineData(MascotMood.Money, MascotMood.MoneyDown)]
    [InlineData(MascotMood.Idle, MascotMood.Sleeping)]
    [InlineData(MascotMood.Waiting, MascotMood.Music)]
    [InlineData(MascotMood.Focused, MascotMood.Victory)]
    [InlineData(MascotMood.Idle, MascotMood.Focused)]
    public void RealExpressionChangesSpin(MascotMood from, MascotMood to) => Assert.True(MascotSpinPolicy.ShouldSpin(from, to));

    [Theory]
    [InlineData(MascotMood.Idle, MascotMood.Waiting)]
    [InlineData(MascotMood.Waiting, MascotMood.Idle)]
    [InlineData(MascotMood.Focused, MascotMood.Worried)]
    [InlineData(MascotMood.Worried, MascotMood.BreakTime)]
    [InlineData(MascotMood.BreakTime, MascotMood.Focused)]
    [InlineData(MascotMood.Electric, MascotMood.Electric)]
    public void IdleBlinksGamingSwapsAndRepaintsDoNot(MascotMood from, MascotMood to) => Assert.False(MascotSpinPolicy.ShouldSpin(from, to));

    [Fact]
    public void TheFirstFaceDoesNotSpin() => Assert.False(MascotSpinPolicy.ShouldSpin(null, MascotMood.Music));
}
