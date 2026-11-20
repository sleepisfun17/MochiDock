using MochiDock.Core;

public class NavigationPolicyTests
{
    [Theory]
    [InlineData(null, "Media")]
    [InlineData("Summary", "Media")]
    [InlineData("invalid", "Media")]
    [InlineData("999", "Media")]
    [InlineData(" hardware ", "Hardware")]
    [InlineData("Settings", "Settings")]
    [InlineData("FxRates", "FxRates")]
    [InlineData("Caffeine", "Media")] // the Caffeine card was removed
    public void RestoredCardIsCanonicalAndKnown(string? saved, string expected) =>
        Assert.Equal(expected, NavigationPolicy.Card(saved, _ => true));

    [Theory]
    [InlineData("Tasks")]
    [InlineData(" tasks ")]
    public void SavedTasksTabLandsOnTodayAfterTheMerge(string saved)
    {
        Assert.Equal("Today", NavigationPolicy.Card(saved, _ => true));
        Assert.Equal("Media", NavigationPolicy.Card(saved, key => key != "Today"));
    }

    [Theory]
    [InlineData("Hardware")][InlineData("Weather")][InlineData("Pomodoro")][InlineData("Prayer")]
    [InlineData("FileShelf")][InlineData("Clipboard")][InlineData("Today")][InlineData("FxRates")]
    public void DisabledCardFallsBackWithoutSelectingAnotherFeature(string card) =>
        Assert.Equal("Media", NavigationPolicy.Card(card, key => key != card));

    [Theory]
    [InlineData(null, "CPU")][InlineData("bad", "CPU")]
    [InlineData("Battery", "Battery")][InlineData(" battery ", "Battery")]
    [InlineData("Games", "Games")][InlineData(" gpu ", "GPU")]
    [InlineData("Memory", "Memory")][InlineData("Drives", "Drives")]
    [InlineData("Network", "Network")]
    public void ModuleValidationPreservesIntentionalChoices(string? saved, string expected) =>
        Assert.Equal(expected, NavigationPolicy.Module(saved));

    [Fact]
    public void ReleaseWithoutPressCannotActivateNewlyAppearedChip()
    {
        var gesture = new SelectionGesture();
        Assert.False(gesture.Release(new object(), true));
    }

    [Fact]
    public void AClickMustStartAndFinishOnSameChipInsideItsBounds()
    {
        var gesture = new SelectionGesture();var cpu = new object();var games = new object();
        gesture.Press(cpu);Assert.False(gesture.Release(games, true));
        gesture.Press(cpu);Assert.False(gesture.Release(cpu, false));
        gesture.Press(games);Assert.True(gesture.Release(games, true));
        Assert.False(gesture.Release(games, true));
        gesture.Press(games);gesture.Cancel();Assert.False(gesture.Release(games, true));
    }
}
