using MochiDock.Core;
using MochiDock.Models;
using Xunit;

namespace MochiDock.Tests;

public class OnboardingPolicyTests
{
    [Fact]
    public void FreshSettings_ShowFirstRunWelcome_UntilDismissed()
    {
        var settings = new AppSettings();
        Assert.False(settings.OnboardingDismissed);
        Assert.True(OnboardingPolicy.ShouldShowFirstRunWelcome(settings.OnboardingDismissed));

        settings.OnboardingDismissed = true;
        Assert.False(OnboardingPolicy.ShouldShowFirstRunWelcome(settings.OnboardingDismissed));
    }

    [Fact]
    public void WhatsAppTrayLink_RoutesToSettingsWhenOptInIsOff()
    {
        Assert.Equal("Set up WhatsApp…", OnboardingPolicy.WhatsAppTrayHeader(false));
        Assert.True(OnboardingPolicy.ShouldOpenWhatsAppSettings(false));

        Assert.Equal("WhatsApp Pairing (GOWA)", OnboardingPolicy.WhatsAppTrayHeader(true));
        Assert.False(OnboardingPolicy.ShouldOpenWhatsAppSettings(true));
    }
}
