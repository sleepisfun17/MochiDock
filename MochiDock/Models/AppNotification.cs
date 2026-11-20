using System.Windows.Media;

namespace MochiDock.Models;

/// <summary>A Windows notification from an app the user chose to watch (Settings > Notifications > Other apps).</summary>
public class AppNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AppName { get; set; } = "";
    public string Aumid { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    /// <summary>The app's logo from Windows, frozen so any thread can read it; null when Windows has none.</summary>
    public ImageSource? Icon { get; set; }
    public bool HasIcon => Icon != null;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string FormattedTime => Timestamp.ToString("HH:mm");
}
