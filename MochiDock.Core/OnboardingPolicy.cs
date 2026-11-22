namespace MochiDock.Core;

/// <summary>Pure rules for first-run welcome visibility and tray WhatsApp link routing.</summary>
public static class OnboardingPolicy
{
    public static bool ShouldShowFirstRunWelcome(bool onboardingDismissed) => !onboardingDismissed;

    public static string WhatsAppTrayHeader(bool enableWhatsApp) =>
        enableWhatsApp ? "WhatsApp Dashboard" : "Set up WhatsApp…";

    public static bool ShouldOpenWhatsAppSettings(bool enableWhatsApp) => !enableWhatsApp;
}
