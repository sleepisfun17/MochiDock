namespace MochiDock.Core;

public static class NavigationPolicy
{
    private static readonly string[] Cards = ["Media", "Email", "WhatsApp", "Discord", "Settings", "Hardware", "Weather", "Pomodoro", "Prayer", "FileShelf", "Clipboard", "Today", "FxRates", "AiUsage", "Script", "Downloads"];
    private static readonly string[] Modules = ["CPU", "GPU", "Memory", "Drives", "Network", "Games", "Battery"];

    public static string Card(string? saved, Func<string, bool> available)
    {
        // The Tasks tab was merged into Today; a saved "Tasks" lands there.
        string? wanted = string.Equals(saved?.Trim(), "Tasks", StringComparison.OrdinalIgnoreCase) ? "Today" : saved?.Trim();
        string? canonical = Cards.FirstOrDefault(x => string.Equals(x, wanted, StringComparison.OrdinalIgnoreCase));
        return canonical is not null && available(canonical) ? canonical : "Media";
    }

    public static string Module(string? saved)
    {
        string? canonical = Modules.FirstOrDefault(x => string.Equals(x, saved?.Trim(), StringComparison.OrdinalIgnoreCase));
        return canonical ?? "CPU";
    }
}
