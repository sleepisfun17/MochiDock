using System.Text.Json;
using MochiDock.Models;

public class GameReadoutSettingsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LegacyPeeksPreferenceRoundTripsWithoutChangingPinOrCapture(bool peeks, bool pin)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            $$"""{"GameOverlayReadoutPeeks":{{peeks.ToString().ToLowerInvariant()}},"IsStatsPinnedToCapsule":{{pin.ToString().ToLowerInvariant()}},"IsHardwareGamingMode":false}""")!;
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(peeks, restored.GameOverlayReadoutPeeks);
        Assert.Equal(pin, restored.IsStatsPinnedToCapsule);
        Assert.False(restored.IsHardwareGamingMode);
    }
}
